using System.Web.Mvc;

namespace AdventureWorksProductAdvisor.Controllers
{
    // Serves the single host page (Views/Home/Index.cshtml) that the chat
    // widget lives on. This is the only MVC controller in the app - AskController
    // and AdminController are Web API controllers, a different base
    // class/pipeline, which is why they live under Controllers/Api instead.
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
