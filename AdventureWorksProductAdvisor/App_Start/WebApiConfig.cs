using System.Web.Http;

namespace AdventureWorksProductAdvisor
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // This is what makes [RoutePrefix]/[Route] attributes on
            // AskController/AdminController actually work - without this
            // call, those attributes would be ignored and Web API would
            // only understand the conventional route registered below.
            config.MapHttpAttributeRoutes();

            // A fallback conventional route (api/{controller}/{id}) - not
            // actually used by anything in this app today, since both Web
            // API controllers use attribute routes exclusively, but kept as
            // the standard template default.
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
