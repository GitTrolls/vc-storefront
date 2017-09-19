﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;

namespace VirtoCommerce.Storefront.Middleware
{
    public class ApiErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiErrorHandlingMiddleware> _logger;
        public ApiErrorHandlingMiddleware(RequestDelegate next, ILogger<ApiErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next.Invoke(context);
            }
            catch (Exception ex)
            {
                //Need handle only storefront api errors
                if (!context.Response.HasStarted && context.Request.Path.ToString().Contains("/storefrontapi/"))
                {
                    _logger.LogError(ex, ex.Message);

                    var message = ex.Message;
                    var httpStatusCode = HttpStatusCode.InternalServerError;
                    //Need to extract AutoRest errors
                    if (ex is HttpOperationException httpException)
                    {
                        message = httpException.Response.Content;
                    }
                    var json = JsonConvert.SerializeObject(new { message = message, stackTrace = ex.StackTrace });
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = (int)httpStatusCode;
                    await context.Response.WriteAsync(json);
                }
                else
                {
                    //Continue default error handling
                    throw ex;
                }
            }
        }
    }

}
