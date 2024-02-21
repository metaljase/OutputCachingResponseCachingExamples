using System.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCaching;

var builder = WebApplication.CreateBuilder(args);

// Add the output caching middleware to the service collection.
builder.Services.AddOutputCache(options =>
{
    // AddBasePolicy overrides the default caching policy (60 secs), and is applied to all endpoints (unless overridden by a specific policy).
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromSeconds(10)));

    // Policy with a tag, so the cached endpoint can be purged, e.g. POST http://localhost:5194/purge/tag-expire
    options.AddPolicy("Tagged20", builder => builder.Expire(TimeSpan.FromSeconds(20)).Tag("tag-expire"));

    // Return a fresh response only if the value for the 'varyOnThis' query parameter changes.
    options.AddPolicy("Vary30", builder =>
    {
        builder.SetVaryByQuery("varyOnThis");
        builder.Expire(TimeSpan.FromSeconds(30));
    });
});

// Add the response caching middleware to the service collection.  Implicitly added when using output caching, so can be omitted.
builder.Services.AddResponseCaching();

var app = builder.Build();

// Add the output caching middleware to the pipeline.  Used to cache responses in the application cache (on server).
// A browser hard-refresh will not force a fresh response from the server (unless the cached response is stale), unlike response caching.
app.UseOutputCache(); // Must be called after 'UseCors', if using CORS.

// Add the response caching middleware to the pipeline.  Used to cache responses in the HTTP cache (on a client, proxy, and/or server).
app.UseResponseCaching(); // Must be called after 'UseCors', if using CORS.


// Response caching CacheControl settings (sets values in Cache-Control header):
// 'Public = true' will add 'public' to the Cache-Control header, false adds nothing.
// 'Private = true' will add 'private' to the Cache-Control header, false adds nothing.
// 'NoCache = true' will add 'no-cache' to the Cache-Control header, false adds nothing.
// 'NoStore = true' will add 'no-store' to the Cache-Control header, false adds nothing.
//
// Behaviour of 'public', 'private', and 'no-store' in the Cache-Control header:
// 'public' allows response to be stored in a cache on the client, proxy, and/or server.
// 'private' allows response to be ONLY stored in a private cache on the client (not proxy or server), even if 'public' is present.
// 'no-cache' allows response to be stored in a cache, but requires revalidation with the server before using a cached response.
// 'no-store' prevents the response from being stored in ANY cache, even if 'public' or 'private' are present...
//   Absence of 'public' AND 'private' AND 'no-store' prevents the response from being stored in ANY cache.
// WARNING: Do no use 'private' in conjunction with output caching because the response will be stored in a cache on the server.

// Adding 'CacheOutput()' or '[OuputCache]' to an endpoint will apply the default caching policy.  Not required if overridden by AddBasePolicy.


// Response stored in output caching middleware: Yes (using the base policy)
// Response stored in response caching middleware: No
// Response stored in a shared cache (proxy/CDN) allowed?: No
// Response stored in a private cache on client allowed?: No
app.MapGet("/", (HttpContext context) =>
{
    var dateTime = DateTime.Now.ToString("O");
    var html = $"""
                    <!DOCTYPE html><html><body style="font-family:Arial, Helvetica, sans-serif;">
                    <a href="/">/</a> | <a href="/public">/public</a> | <a href="/private">/private</a> | <a href="/no-cache">/no-cache</a> | <a href="/no-store">/no-store</a>
                    <pre style="font-size:20px; font-weight:bold;">Generated at: {dateTime}</pre>
                    <p>The generated date/time above is also output to Visual Studio's output window for comparison.</p>
                    <p>TIP: Open the browser's developer tools and monitor the 'Network' tab to see the caching behaviour, especially the Cache-Control header.</p>
                    <p>NOTE: This page is cached for 10 seconds.</p>
                    <p>This page is only cached on the server, using output caching.  Unlike response caching, a browser hard-refresh will not force a fresh response from the server (unless the cached response is stale).</p>
                    </body></html>
                    """;

    Debug.WriteLine($"{dateTime} {context.Request.GetDisplayUrl()}");
    return Results.Text(html, "text/html");
});

// Response stored in output caching middleware: Yes (using the 'Vary30' policy)
// Response stored in response caching middleware: Yes
// Response stored in a shared cache (proxy/CDN) allowed?: Yes
// Response stored in a private cache on client allowed?: Yes
app.MapGet("/public", [OutputCache(PolicyName = "Vary30")] (int? varyOnThis, string? random, HttpContext context) =>
{
    var dateTime = DateTime.Now.ToString("O");

    context.Response.GetTypedHeaders().CacheControl = new()
    {
        Public = true, // Allow response to be stored in a cache on the client, proxy, and/or server.
        MaxAge = TimeSpan.FromSeconds(30)
    };

    // Configure the response caching middleware.
    // Changes to the 'varyOnThis' query parameter will return a fresh response, changing other query parameters will return the cached response.
    if (context.Features.Get<IResponseCachingFeature>() is { } responseCaching)
    {
        responseCaching.VaryByQueryKeys = ["varyOnThis"];
    }

    var html = $"""
                    <!DOCTYPE html><html><body style="font-family:Arial, Helvetica, sans-serif;">
                    <a href="/">/</a> | <a href="/public">/public</a> | <a href="/private">/private</a> | <a href="/no-cache">/no-cache</a> | <a href="/no-store">/no-store</a>
                    <pre style="font-size:20px; font-weight:bold;">Generated at: {dateTime}</pre>
                    <p>The generated date/time above is also output to Visual Studio's output window for comparison.</p>
                    <p>TIP: Open the browser's developer tools and monitor the 'Network' tab to see the caching behaviour, especially the Cache-Control header.</p>
                    <p>NOTE: This page is cached for 30 seconds.</p>
                    <ol>
                    <li><a href="/public?varyOnThis=100&random=1">/public?varyOnThis=100&random=1</a> This will return a fresh response from the server, which will be stored in all caches.</li>
                    <li><a href="/public?varyOnThis=100&random=1">/public?varyOnThis=100&random=1</a> This identical URL will return the stored response from the browser cache.</li>
                    <li><a href="/public?varyOnThis=100&random=2">/public?varyOnThis=100&random=2</a> Because the value of 'random' is different, no response from the browser cache will be used, so a request will be sent to the server.  The server has been configured to 'vary' on the 'varyOnThis' query parameter (ignoring 'random'), so the response will be returned from the server cache (unless stale).</li>
                    <li><a href="/public?varyOnThis=200&random=1">/public?varyOnThis=200&random=1</a> The 'varyOnThis' value is different, therefore no response from the browser cache will be used, so a request will be sent to the server which will return a fresh response.</li>
                    <li><a href="/public?varyOnThis=200&random=2">/public?varyOnThis=200&random=2</a> Same behaviour as the 3rd step.</li>
                    </ol>
                    </body></html>
                    """;

    Debug.WriteLine($"{dateTime} {context.Request.GetDisplayUrl()}");
    return Results.Text(html, "text/html");
});

// Response stored in output caching middleware: No
// Response stored in response caching middleware: No
// Response stored in a shared cache (proxy/CDN) allowed?: No
// Response stored in a private cache on client allowed?: Yes
app.MapGet("/private", (HttpContext context) =>
{
    var dateTime = DateTime.Now.ToString("O");

    context.Response.GetTypedHeaders().CacheControl = new()
    {
        Private = true, // Allow response to be stored only in a private cache on the client.
        MaxAge = TimeSpan.FromSeconds(40)
    };

    var html = $"""
                    <!DOCTYPE html><html><body style="font-family:Arial, Helvetica, sans-serif;">
                    <a href="/">/</a> | <a href="/public">/public</a> | <a href="/private">/private</a> | <a href="/no-cache">/no-cache</a> | <a href="/no-store">/no-store</a> 
                    <pre style="font-size:20px; font-weight:bold;">Generated at: {dateTime}</pre>
                    <p>The generated date/time above is also output to Visual Studio's output window for comparison.</p>
                    <p>TIP: Open the browser's developer tools and monitor the 'Network' tab to see the caching behaviour, especially the Cache-Control header.</p>
                    <p>NOTE: This page is cached for 40 seconds in the browser's cache only.</p>
                    <ol>
                    <li><a href="/private">/private</a> This identical URL will return the stored response from the browser cache (unless stale).</li>
                    <li>Perform a browser hard-refresh (ctrl+F5) which will send a request to the server for a fresh response.  Response caching on the server will return a fresh response, unlike output caching (which is disabled for this endpoint).</li>
                    </ol>
                    </body></html>
                    """;

    Debug.WriteLine($"{dateTime} {context.Request.GetDisplayUrl()}");
    return Results.Text(html, "text/html");
}).CacheOutput(builder => builder.NoCache());

// Response stored in output caching middleware: Yes (using the 'Tagged20' policy)
// Response stored in response caching middleware: Yes
// Response stored in a shared cache (proxy/CDN) allowed?: Yes
// Response stored in a private cache on client allowed?: Yes
app.MapGet("/no-cache", (HttpContext context) =>
{
    var dateTime = DateTime.Now.ToString("O");

    context.Response.GetTypedHeaders().CacheControl = new()
    {
        Public = true, // Allow response to be stored in a cache on the client, proxy, and/or server.
        NoCache = true, // Revalidate the response with the server before using a cached response.
        MaxAge = TimeSpan.FromSeconds(20)
    };

    // Set the Last-Modified header to the current date and time.  The browser will return the Last-Modified value in the
    // If-Modified-Since header for any subsequent requests to determine if its cached response is still fresh or stale.
    // If its cached response is fresh, the server will return a 304 Not Modified status, and will use its cached response.
    // If its cached response is stale, the server will return a fresh response with a 200 OK status.
    context.Response.GetTypedHeaders().LastModified = DateTime.UtcNow;

    var html = $"""
                    <!DOCTYPE html><html><body style="font-family:Arial, Helvetica, sans-serif;">
                    <a href="/">/</a> | <a href="/public">/public</a> | <a href="/private">/private</a> | <a href="/no-cache">/no-cache</a> | <a href="/no-store">/no-store</a>
                    <pre style="font-size:20px; font-weight:bold;">Generated at: {dateTime}</pre>
                    <p>The generated date/time above is also output to Visual Studio's output window for comparison.</p>
                    <p>TIP: Open the browser's developer tools and monitor the 'Network' tab to see the caching behaviour, especially the Cache-Control header.</p>
                    <p>NOTE: This page is cached for 20 seconds.</p>
                    <p>Perhaps surprisingly, the 'no-cache' Cache-Control header does not prevent the response from being stored in a cache (unlike 'no-store').  However, before the browser uses its cached response, it must check (revalidate) with the server that its cached response is still fresh.  If it's still fresh, the server returns a '304 Not Modified' status and the browser will use its cached response.  If stale, the server will return a fresh response.</p>
                    <ul>
                    <li>If a response includes the 'Last-Modified' header, the browser will include that 'Last-Modified' value in the 'If-Modified-Since' header for revalidation requests.  The server compares that value with the 'Last-Modified' value in the server cache to determine if the browser's cached response is fresh or stale.</li>
                    <li>If a response includes the 'ETag' header, the browser will include that 'ETag' value in the 'If-None-Match' header for revalidation requests.  The server compares that value with the 'ETag' value in the server cache to determine if the browser's cached response is fresh or stale.</li>
                    </ul>
                    <ol>
                    <li><a href="/no-cache">/no-cache</a> This identical URL will send a request to the server for revalidation, and either a fresh response will be returned or the browser's cached response will be used (if still fresh).</li>
                    </ol>
                    </body></html>
                    """;

    Debug.WriteLine($"{dateTime} {context.Request.GetDisplayUrl()}");
    return Results.Text(html, "text/html");
}).CacheOutput("Tagged20");

// Response stored in output caching middleware: No
// Response stored in response caching middleware: No
// Response stored in a shared cache (proxy/CDN) allowed?: No
// Response stored in a private cache on client allowed?: No
app.MapGet("/no-store", (HttpContext context) =>
{
    var dateTime = DateTime.Now.ToString("O");

    context.Response.GetTypedHeaders().CacheControl = new()
    {
        NoStore = true // Do not store the response in any cache.
    };

    var html = $"""
                    <!DOCTYPE html><html><body style="font-family:Arial, Helvetica, sans-serif;">
                    <a href="/">/</a> | <a href="/public">/public</a> | <a href="/private">/private</a> | <a href="/no-cache">/no-cache</a> | <a href="/no-store">/no-store</a>
                    <pre style="font-size:20px; font-weight:bold;">Generated at: {dateTime}</pre>
                    <p>The generated date/time above is also output to Visual Studio's output window for comparison.</p>
                    <p>TIP: Open the browser's developer tools and monitor the 'Network' tab to see the caching behaviour, especially the Cache-Control header.</p>
                    <p>NOTE: This page is not cached.</p>
                    <ol>
                    <li><a href="/no-store">/no-store</a> This identical URL will send a request to the server, which will return a fresh response.</li>
                    </ol>
                    </body></html>
                    """;

    Debug.WriteLine($"{dateTime} {context.Request.GetDisplayUrl()}");
    return Results.Text(html, "text/html");
}).CacheOutput(builder => builder.NoCache());

// This endpoint will evict cache(s) for endpoints with specified tag, e.g. POST http://localhost:5062/purge/tag-expire
app.MapPost("/purge/{tag}", async (IOutputCacheStore cache, string tag) =>
{
    await cache.EvictByTagAsync(tag, default);
});

await app.RunAsync();
