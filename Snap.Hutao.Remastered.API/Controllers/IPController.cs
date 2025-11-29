using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Snap.Hutao.Remastered.API.Controllers
{
    [Route("ips")]
    [ApiController]
    public class IPController : ControllerBase
    {
        [HttpGet]
        public ActionResult<string> Get()
        {
            // Prefer X-Forwarded-For when behind a proxy/load balancer
            var forwarded = Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                // may contain a list of IPs, take the first
                var first = forwarded.Split(',').Select(p => p.Trim()).FirstOrDefault();
                if (!string.IsNullOrEmpty(first))
                {
                    return Ok(first);
                }
            }

            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (remoteIp == "::1")
            {
                remoteIp = "127.0.0.1";
            }

            return Ok(remoteIp ?? "unknown");
        }
    }
}
