# triaxis.AspNetCore.ReverseProxy

[![Build Status](https://travis-ci.com/ssimek/triaxis.AspNetCore.ReverseProxy.svg?branch=master)](https://travis-ci.com/ssimek/triaxis.AspNetCore.ReverseProxy)

Simple and efficient ASP.NET Core middleware that forwards all requests with a specific prefix to another server

## Usage

Add the following at the appropriate place in your middleware chain

```C#
app.UseReverseProxy("/api", new Uri("http://internal-api-server/api"));
```

## License

This package is licensed under the [MIT License](./LICENSE.txt)

Copyright &copy; 2019 triaxis s.r.o.
