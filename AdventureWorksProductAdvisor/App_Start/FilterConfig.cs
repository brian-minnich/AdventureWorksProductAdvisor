using System.Web.Mvc;

namespace AdventureWorksProductAdvisor
{
    // Standard MVC template boilerplate: HandleErrorAttribute shows a
    // generic error view instead of a raw exception page for unhandled
    // exceptions in MVC controllers (HomeController). Note this doesn't
    // apply to the Web API controllers (AskController/AdminController) -
    // those handle their own errors directly (see AskController's try/catch).
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }
}
