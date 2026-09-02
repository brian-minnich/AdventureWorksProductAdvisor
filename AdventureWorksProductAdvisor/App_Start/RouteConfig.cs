using System.Web.Mvc;
using System.Web.Routing;

namespace AdventureWorksProductAdvisor
{
    // Registers routing for MVC controllers (HomeController) only. The Web
    // API controllers (AskController, AdminController) use attribute
    // routing instead - see WebApiConfig.cs - so they don't go through this
    // "Default" route at all.
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            // Skip routing for .axd resource requests (e.g. WebResource.axd) - standard boilerplate.
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Falls back to HomeController.Index for any URL that doesn't
            // match something more specific - since this app has exactly
            // one MVC page, in practice every browser request for the site
            // itself lands here.
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
