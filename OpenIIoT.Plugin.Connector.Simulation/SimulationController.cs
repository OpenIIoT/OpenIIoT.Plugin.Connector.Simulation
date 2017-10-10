using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace OpenIIoT.Plugin.Connector.Simulation
{
    [WebApiRoutePrefix("v2/simulation")]
    public class SimulationController : ApiController
    {
        #region Public Methods

        [Route("get")]
        [HttpGet]
        public string Get()
        {
            return "Simulation connector!";
        }

        #endregion Public Methods
    }
}