using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace FileManagerServer.Controllers
{
    [Route("web")]
    public class WebInterfaceController : ControllerBase
    {
        // Serve the HTML file at the /web route
        [HttpGet]
        public IActionResult Index()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "index.html");
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Web interface file not found.");
            }

            var htmlContent = System.IO.File.ReadAllText(filePath);
            return Content(htmlContent, "text/html");
        }

        // Serve the JavaScript file at the /web/module-231fewg341h23j12.js route
        [HttpGet("module-231fewg341h23j12.js")]
        public IActionResult GetJavaScriptFile()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "module-231fewg341h23j12.js");
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("JavaScript file not found.");
            }

            return PhysicalFile(filePath, "application/javascript");
        }
    }
}