using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using PagedList.Core;
using VirtoCommerce.Storefront.AutoRestClients.CustomerModuleApi;
using VirtoCommerce.Storefront.Infrastructure;
using VirtoCommerce.Storefront.Model;
using VirtoCommerce.Storefront.Model.Caching;
using VirtoCommerce.Storefront.Model.Common;
using VirtoCommerce.Storefront.Model.Common.Caching;
using VirtoCommerce.Storefront.Model.Customer;
using VirtoCommerce.Storefront.Model.Customer.Services;
using VirtoCommerce.Storefront.Model.Stores;
using customerDto = VirtoCommerce.Storefront.AutoRestClients.CustomerModuleApi.Models;

namespace VirtoCommerce.Storefront.Domain
{
    public class MemberService : IMemberService
    {
        private readonly ICustomerModule _customerApi;
        private readonly IStorefrontMemoryCache _memoryCache;
        private readonly IApiChangesWatcher _apiChangesWatcher;

        public MemberService(ICustomerModule customerApi, IStorefrontMemoryCache memoryCache, IApiChangesWatcher changesWatcher)
        {
            _customerApi = customerApi;
            _memoryCache = memoryCache;
            _apiChangesWatcher = changesWatcher;
        }

        #region ICustomerService Members
        public virtual async Task<Contact> GetContactByIdAsync(string contactId)
        {
            if (contactId == null)
            {
                throw new ArgumentNullException(nameof(contactId));
            }

            Contact result = null;
            var cacheKey = CacheKey.With(GetType(), "GetContactByIdAsync", contactId);
            var dto = await _memoryCache.GetOrCreateExclusiveAsync(cacheKey, async (cacheEntry) =>
            {
                var contactDto = await _customerApi.GetContactByIdAsync(contactId);
                if (contactDto != null)
                {
                    cacheEntry.AddExpirationToken(CustomerCacheRegion.CreateChangeToken(contactDto.Id));
                    cacheEntry.AddExpirationToken(_apiChangesWatcher.CreateChangeToken());
                }
                return contactDto;
            });

            if (dto != null)
            {
                result = dto.ToContact();
                if (!dto.Organizations.IsNullOrEmpty())
                {
                    //Load contact organization
                    result.Organization = await GetOrganizationByIdAsync(dto.Organizations.FirstOrDefault());
                }
            }
            return result;
        }

        public virtual async Task<Contact> CreateContactAsync(Contact contact)
        {
            var contactDto = contact.ToContactDto();
            var result = await _customerApi.CreateContactAsync(contactDto);
            return result?.ToContact();
        }


        public virtual async Task DeleteContactAsync(string contactId)
        {
            await _customerApi.DeleteContactsAsync(new[] { contactId });
            //Invalidate cache
            CustomerCacheRegion.ExpireMember(contactId);
        }


        public virtual async Task UpdateContactAsync(Contact contact)
        {
            await _customerApi.UpdateContactAsync(contact.ToContactDto());
            //Invalidate cache
            CustomerCacheRegion.ExpireMember(contact.Id);
        }

        public virtual async Task UpdateContactAddressesAsync(string contactId, IList<Address> addresses)
        {
            var existContact = await GetContactByIdAsync(contactId);
            if (existContact != null)
            {
                await _customerApi.UpdateAddessesAsync(addresses.Select(x => x.ToCustomerAddressDto()).ToList(), contactId);

                //Invalidate cache
                CustomerCacheRegion.ExpireMember(existContact.Id);
            }
        }

        public virtual async Task<Vendor[]> GetVendorsByIdsAsync(Store store, Language language, params string[] vendorIds)
        {
            var cacheKey = CacheKey.With(GetType(), "GetVendorsByIdsAsync", string.Join("-", vendorIds.OrderBy(x => x)));
            var result = await _memoryCache.GetOrCreateExclusiveAsync(cacheKey, async (cacheEntry) =>
            {
                return await _customerApi.GetVendorsByIdsAsync(vendorIds);
            });
            return result?.Select(x => x.ToVendor(language, store)).ToArray();
        }

        public virtual Vendor[] GetVendorsByIds(Store store, Language language, params string[] vendorIds)
        {
            return GetVendorsByIdsAsync(store, language, vendorIds).GetAwaiter().GetResult();
        }

        public virtual IPagedList<Vendor> SearchVendors(Store store, Language language, string keyword, int pageNumber, int pageSize, IEnumerable<SortInfo> sortInfos)
        {
            // TODO: implement indexed search for vendors
            var criteria = new customerDto.MembersSearchCriteria
            {
                SearchPhrase = keyword,
                DeepSearch = true,
                Skip = (pageNumber - 1) * pageSize,
                Take = pageSize
            };
            if (!sortInfos.IsNullOrEmpty())
            {
                criteria.Sort = SortInfo.ToString(sortInfos);
            }
            var cacheKey = CacheKey.With(GetType(), "SearchVendors", keyword, pageNumber.ToString(), pageSize.ToString(), criteria.Sort);
            var result = _memoryCache.GetOrCreateExclusive(cacheKey, cacheEntry =>
            {
                return _customerApi.SearchVendors(criteria);
            });
            var vendors = result.Vendors.Select(x => x.ToVendor(language, store));
            return new StaticPagedList<Vendor>(vendors, pageNumber, pageSize, result.TotalCount.Value);
        }

        public async Task<Organization> GetOrganizationByIdAsync(string organizationId)
        {
            Organization result = null;
            var cacheKey = CacheKey.With(GetType(), "GetOrganizationByIdAsync", organizationId);
            var dto = await _memoryCache.GetOrCreateExclusiveAsync(cacheKey, async (cacheEntry) =>
            {
                var organizationDto = await _customerApi.GetOrganizationByIdAsync(organizationId);
                if (organizationDto != null)
                {
                    cacheEntry.AddExpirationToken(CustomerCacheRegion.CreateChangeToken(organizationDto.Id));
                    cacheEntry.AddExpirationToken(_apiChangesWatcher.CreateChangeToken());
                }
                return organizationDto;
            });

            if (dto != null)
            {
                result = dto.ToOrganization();

                //Lazy load organization contacts
                result.Contacts = new MutablePagedList<Contact>((pageNumber, pageSize, sortInfos, @params) =>
                {
                    var criteria = new OrganizationContactsSearchCriteria
                    {
                        OrganizationId = result.Id,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                    if (!sortInfos.IsNullOrEmpty())
                    {
                        criteria.Sort = SortInfo.ToString(sortInfos);
                    }
                    if (@params != null)
                    {
                        criteria.CopyFrom(@params);
                    }
                    return SearchOrganizationContacts(criteria);

                }, 1, 20);
            }
            return result;
        }


        public async Task<Organization> CreateOrganizationAsync(Organization organization)
        {
            var orgDto = organization.ToOrganizationDto();
            var result = await _customerApi.CreateOrganizationAsync(orgDto);
            return result?.ToOrganization();
        }

        public async Task UpdateOrganizationAsync(Organization organization)
        {
            var orgDto = organization.ToOrganizationDto();
            await _customerApi.UpdateOrganizationAsync(orgDto);
            CustomerCacheRegion.ExpireMember(organization.Id);
        }

        public IPagedList<Contact> SearchOrganizationContacts(OrganizationContactsSearchCriteria criteria)
        {
            return SearchOrganizationContactsAsync(criteria).GetAwaiter().GetResult();
        }

        public async Task<IPagedList<Contact>> SearchOrganizationContactsAsync(OrganizationContactsSearchCriteria criteria)
        {
            var searchResult = await _customerApi.SearchContactsAsync(
                memberId: criteria.OrganizationId,
                skip: (criteria.PageNumber - 1) * criteria.PageSize,
                take: criteria.PageSize,
                sort: criteria.Sort,
                searchPhrase: criteria.SearchPhrase,
                objectType: "Member");
            var contacts = searchResult.Results.Select(x => x.ToContact()).ToList();

            return new StaticPagedList<Contact>(contacts, criteria.PageNumber, criteria.PageSize, searchResult.TotalCount ?? 0);
        }
        #endregion
    }
}

