using AngleSharp.Html.Parser;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Requestr.Web.Components.FormBuilder;
using Xunit;

namespace Requestr.Web.Tests.Services;

public class StaticAssetTests
{
    internal static string WebRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Requestr.Web"));

    [Fact]
    public async Task RenderedLayoutVersionsEveryLocalStylesheetAndScript()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(DesignerWorkspace).Assembly.GetName().Name,
            ContentRootPath = WebRoot,
            WebRootPath = Path.Combine(WebRoot, "wwwroot"),
            EnvironmentName = "Development"
        });
        builder.WebHost.UseStaticWebAssets();
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        var context = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var engine = services.GetRequiredService<IRazorViewEngine>();
        var view = engine.GetView(null, "/Pages/Shared/_Layout.cshtml", false).View;
        Assert.NotNull(view);
        using var writer = new StringWriter();
        var viewContext = new ViewContext(context, view, new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
        var page = (RazorPage)services.GetRequiredService<IRazorPageFactoryProvider>().CreateFactory("/Pages/Shared/_Layout.cshtml").RazorPageFactory!();
        page.ViewContext = viewContext;
        page.BodyContent = new HtmlString("");
        services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext);
        await services.GetRequiredService<IComponentPrerenderer>().Dispatcher.InvokeAsync(page.ExecuteAsync);
        var document = new HtmlParser().ParseDocument(writer.ToString());
        var paths = document.QuerySelectorAll("link[rel='stylesheet'], script[src]")
            .Select(element => element.GetAttribute(element.LocalName == "link" ? "href" : "src")!)
            .Where(path => !path.StartsWith("https://", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.Contains("?v=", path));
        var css = File.ReadAllText(Path.Combine(WebRoot, "obj/Debug/net10.0/scopedcss/bundle/Requestr.Web.styles.css"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(WebRoot, "bin/Debug/net10.0/Requestr.Web.staticwebassets.endpoints.json")));
        var routes = manifest.RootElement.GetProperty("Endpoints").EnumerateArray().Select(endpoint => endpoint.GetProperty("Route").GetString()).ToHashSet();
        foreach (Match import in Regex.Matches(css, "@import ['\"]([^'\"]+)['\"]"))
        {
            var path = import.Groups[1].Value;
            Assert.Matches(@"\.[a-z0-9]{10}\.bundle\.scp\.css$", path);
            Assert.Contains(path, routes);
        }
    }
}