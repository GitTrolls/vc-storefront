﻿using System.IO;
using System.Text.Encodings.Web;
using DotLiquid;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace VirtoCommerce.LiquidThemeEngine.Tags
{
    /// <summary>
    /// Tag for Antiforgery token generation. Usage: {% anti_forgery %}
    /// </summary>
    public class AntiforgeryTag : Tag
    {
        public override void Render(Context context, TextWriter result)
        {
            var themeAdaptor = (ShopifyLiquidThemeEngine)Template.FileSystem;
            var httpContext = themeAdaptor.HttpContext;
            var antiforgery = httpContext.RequestServices.GetService<IAntiforgery>();            
            var htmlContent = antiforgery.GetHtml(httpContext);
            htmlContent.WriteTo(result, HtmlEncoder.Default);
        }
    }
}
