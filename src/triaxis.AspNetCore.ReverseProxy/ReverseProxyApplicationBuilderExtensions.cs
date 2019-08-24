using System;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using triaxis.AspNetCore.ReverseProxy;

namespace Microsoft.Extensions.Configuration
{
    /// <summary>
    /// Provides extensions for <see cref="IApplicationBuilder"/>
    /// </summary>
    public static class ReverseProxyApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds a reverse-proxying middleware to the chain
        /// </summary>
        public static IApplicationBuilder UseReverseProxy(this IApplicationBuilder builder, string localPath, Uri remoteUri, Action<HttpClientHandler> handlerInit = null)
            => builder.Use(next => new ProxyMiddleware(next, localPath, remoteUri, handlerInit).InvokeAsync);
    }
}
