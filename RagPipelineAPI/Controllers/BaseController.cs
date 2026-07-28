using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace SimpleChatbot.Controllers
{
    public class BaseController : ControllerBase
    {
        private ILogger? _logger;

        protected ILogger Logger =>
            _logger ??= HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(GetType());

    }
}
