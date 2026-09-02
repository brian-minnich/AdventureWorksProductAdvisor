using System.Web.Mvc;

namespace AdventureWorksProductAdvisor
{
    // Standard MVC template boilerplate: HandleErrorAttribute shows a
    // generic error view instead of a raw exception page for unhandled
    // exceptions in MVC controllers (HomeController). Note this doesn't
    // apply to the Web API controllers - AskController.Post and
    // AdminController.BackfillEmbeddings each wrap their own logic in a
    // try/catch instead, since a Web API action's exceptions aren't routed
    // through this MVC filter at all.
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }
}
