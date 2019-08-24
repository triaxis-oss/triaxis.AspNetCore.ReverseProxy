using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace triaxis.AspNetCore.ReverseProxy
{
    /// <summary>
    /// Middleware forwarding complete requests starting with
    /// the specified path to another sever
    /// </summary>
    class ProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PathString _localPath;
        private readonly HttpClient _client;
        private readonly string _basePath;

        public ProxyMiddleware(RequestDelegate next, string localPath, Uri remoteUri, Action<HttpClientHandler> handlerInit)
        {
            _next = next;
            _localPath = new PathString(localPath);

            var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = false,
            };

            handlerInit?.Invoke(handler);

            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri(remoteUri.GetLeftPart(UriPartial.Authority)),
            };

            _basePath = remoteUri.AbsolutePath;
        }

        /// <summary>
        /// Headers that are never forwarded (hop-to-hop)
        /// </summary>
        static readonly HashSet<string> s_noForwardHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host",
            "Keep-Alive",
            "Transfer-Encoding",
            "TE",
            "Connection",
            "Trailer",
            "Upgrade",
        };

        /// <summary>
        /// Map of supported HTTP methods
        /// </summary>
        static readonly Dictionary<string, (HttpMethod method, bool hasContent)> s_methodMap = new Dictionary<string, (HttpMethod method, bool hasContent)>(StringComparer.OrdinalIgnoreCase)
        {
            [HttpMethods.Delete] = (HttpMethod.Delete, false),
            [HttpMethods.Get] = (HttpMethod.Get, false),
            [HttpMethods.Head] = (HttpMethod.Head, false),
            [HttpMethods.Options] = (HttpMethod.Options, false),
            [HttpMethods.Patch] = (new HttpMethod("PATCH"), true),
            [HttpMethods.Post] = (HttpMethod.Post, true),
            [HttpMethods.Put] = (HttpMethod.Put, true),
            [HttpMethods.Trace] = (HttpMethod.Trace, false),
        };

        /// <summary>
        /// Main middleware handler, forwards matching requests to <see cref="ProcessRequestAsync"/>
        /// </summary>
        /// <param name="context">The <see cref="HttpContext"/> of the request</param>
        public Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments(_localPath, out var requestPath) ||
                !s_methodMap.TryGetValue(context.Request.Method, out var method))
            {
                return _next(context);
            }

            return ProcessRequestAsync(context, requestPath, method.method, method.hasContent);
        }

        /// <summary>
        /// Forwards the request to the configured URI
        /// </summary>
        /// <param name="context">The <see cref="HttpContext"/> of the request</param>
        /// <param name="requestPath">The part of the path of the incoming request to be forwarded</param>
        /// <param name="method">The <see cref="HttpMethod"/> of the request</param>
        /// <param name="content">Indicates the <paramref name="method"/> includes content</param>
        /// <returns></returns>
        private async Task ProcessRequestAsync(HttpContext context, PathString requestPath, HttpMethod method, bool content)
        {
            var requestMessage = BuildRequestMessage(context.Request, requestPath, method, content);

            using (var response = await _client.SendAsync(requestMessage))
            {
                context.Response.StatusCode = (int)response.StatusCode;

                foreach (var header in response.Headers)
                {
                    if (s_noForwardHeaders.Contains(header.Key))
                    {
                        continue;
                    }

                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));
                }

                foreach (var header in response.Content.Headers)
                {
                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));
                }

                await response.Content.CopyToAsync(context.Response.Body);
            }
        }

        /// <summary>
        /// Builds a <see cref="HttpRequestMessage"/> for the forwarded request
        /// </summary>
        /// <param name="request">The incoming <see cref="HttpRequest"/></param>
        /// <param name="requestPath">The part of the path of the incoming request to be forwarded</param>
        /// <param name="method">The <see cref="HttpMethod"/> of the request</param>
        /// <param name="content">Indicates the <paramref name="method"/> includes content</param>
        /// <returns>A <see cref="HttpRequestMessage"/> for the forwarded request</returns>
        private HttpRequestMessage BuildRequestMessage(HttpRequest request, PathString requestPath, HttpMethod method, bool content)
        {
            var path = _basePath + requestPath + request.QueryString;

            var message = new HttpRequestMessage(method, path);

            if (content)
            {
                message.Content = new ForwardedHttpContent(request);
            }

            foreach (var header in request.Headers)
            {
                if (s_noForwardHeaders.Contains(header.Key))
                {
                    continue;
                }

                // content headers must be added to the content object,
                // adding them to request.Headers throws an exception
                if (content && header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content.Headers.Add(header.Key, (IEnumerable<string>)header.Value);
                }
                else
                {
                    message.Headers.Add(header.Key, (IEnumerable<string>)header.Value);
                }
            }

            return message;
        }

        /// <summary>
        /// <see cref="HttpContent"/> implementation providing efficient
        /// forwarding of a <see cref="HttpRequest"/>.<see cref="HttpRequest.Body"/>
        /// </summary>
        private class ForwardedHttpContent : HttpContent
        {
            HttpRequest _request;

            public ForwardedHttpContent(HttpRequest request)
            {
                _request = request;
            }

            protected override Task<Stream> CreateContentReadStreamAsync()
            {
                return Task.FromResult(_request.Body);
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return _request.Body.CopyToAsync(stream);
            }

            protected override bool TryComputeLength(out long length)
            {
                long? requestLength = _request.ContentLength;
                length = requestLength ?? 0;
                return requestLength != null;
            }
        }
    }
}
