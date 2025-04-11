using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NWebDav.Server;
using NWebDav.Server.Http;
using NWebDav.Server.Stores;

namespace FileManagerServer.Controllers
{
    [Route("webdav/{*path}")]
    public class WebDAVController : ControllerBase
    {
        private readonly IStore _store;
        private readonly WebDavDispatcher _dispatcher;

        public WebDAVController()
        {
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "UserData");
            _store = new DiskStore(basePath);
            _dispatcher = new WebDavDispatcher(_store);
        }

        [AcceptVerbs("GET", "PUT", "POST", "DELETE", "HEAD", "OPTIONS", "PROPFIND", "PROPPATCH", "LOCK", "UNLOCK", "COPY", "MOVE", "MKCOL")]
        public async Task<IActionResult> HandleRequest(string path)
        {
            var httpContext = new AspNetHttpContext(HttpContext);
            await _dispatcher.DispatchRequestAsync(httpContext);
            return new EmptyResult();
        }
    }

    // Адаптер для HttpContext
    public class AspNetHttpContext : IHttpContext
    {
        private readonly Microsoft.AspNetCore.Http.HttpContext _context;

        public AspNetHttpContext(Microsoft.AspNetCore.Http.HttpContext context)
        {
            _context = context;
        }

        public IHttpRequest Request => new AspNetHttpRequest(_context.Request);
        public IHttpResponse Response => new AspNetHttpResponse(_context.Response);

        public IHttpSession Session => null;

        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }
    }

    public class AspNetHttpRequest : IHttpRequest
    {
        private readonly Microsoft.AspNetCore.Http.HttpRequest _request;

        public AspNetHttpRequest(Microsoft.AspNetCore.Http.HttpRequest request)
        {
            _request = request;
        }

        public string HttpMethod => _request.Method;
        public Stream InputStream => _request.Body;
        public Uri Url => new Uri(_request.Scheme + "://" + _request.Host + _request.Path + _request.QueryString);
        public string RemoteEndPoint => _request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        public IEnumerable<string> Headers => _request.Headers.Select(h => h.Key);
        public string GetHeaderValue(string header) => _request.Headers[header].FirstOrDefault();
        public Stream Stream => InputStream;
    }

    public class AspNetHttpResponse : IHttpResponse
    {
        private readonly Microsoft.AspNetCore.Http.HttpResponse _response;

        public AspNetHttpResponse(Microsoft.AspNetCore.Http.HttpResponse response)
        {
            _response = response;
        }

        public int Status
        {
            get => _response.StatusCode;
            set => _response.StatusCode = value;
        }

        public string StatusDescription
        {
            get => null;
            set { }
        }

        public Stream OutputStream => _response.Body;

        public Stream Stream => OutputStream;

        public void SetHeaderValue(string header, string value)
        {
            _response.Headers[header] = value;
        }
    }
}