using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace FileManagerServer.Controllers
{
    [Route("api/plugins")]
    [ApiController]
    public class PluginsController : ControllerBase
    {
        private static readonly string PluginsPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
        private static readonly List<IPlugin> LoadedPlugins = new List<IPlugin>();

        static PluginsController()
        {
            if (!Directory.Exists(PluginsPath))
                Directory.CreateDirectory(PluginsPath);
        }

        [HttpGet]
        public IActionResult GetPlugins()
        {
            var pluginFiles = Directory.GetFiles(PluginsPath, "*.dll");
            return Ok(pluginFiles.Select(Path.GetFileName));
        }

        [HttpPost("load")]
        public IActionResult LoadPlugin([FromQuery] string pluginName)
        {
            var pluginPath = Path.Combine(PluginsPath, pluginName);
            if (!System.IO.File.Exists(pluginPath))
                return NotFound();

            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
                var pluginTypes = assembly.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t));

                foreach (var type in pluginTypes)
                {
                    var plugin = (IPlugin)Activator.CreateInstance(type);
                    LoadedPlugins.Add(plugin);
                    plugin.Initialize();
                }

                return Ok($"Loaded {pluginTypes.Count()} plugins from {pluginName}");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error loading plugin: {ex.Message}");
            }
        }

        [HttpGet("execute/{pluginName}")]
        public IActionResult ExecutePlugin([FromRoute] string pluginName, [FromQuery] string username, [FromQuery] string spaceName)
        {
            var plugin = LoadedPlugins.FirstOrDefault(p => p.Name == pluginName);
            if (plugin == null)
                return NotFound("Plugin not loaded");

            try
            {
                var result = plugin.Execute(username, spaceName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error executing plugin: {ex.Message}");
            }
        }
    }

    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        void Initialize();
        object Execute(string username, string spaceName);
    }
}