using System;
using System.Net;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.LindenCaps
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "BadgeModule")]
    public class BadgeModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;
        private bool m_enabled;
        private IAvatarBadgeModule m_badgeModule;

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
                return;

            string setting = config.GetString("Cap_SetBadge", string.Empty);
            if (setting == "localhost")
                m_enabled = true;

            if (m_enabled)
                m_log.Info("[BADGES] Viewer badge capability enabled.");
        }

        public void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            m_scene = scene;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            m_badgeModule = scene.RequestModuleInterface<IAvatarBadgeModule>();
            if (m_badgeModule is null)
            {
                m_log.Warn("[BADGES] Disabling badge capability - profile module does not provide badge support.");
                m_enabled = false;
                return;
            }

            scene.EventManager.OnRegisterCaps += OnRegisterCaps;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            scene.EventManager.OnRegisterCaps -= OnRegisterCaps;
            m_scene = null;
            m_badgeModule = null;
        }

        public void PostInitialise() { }

        public void Close() { }

        public string Name => "BadgeModule";

        public Type ReplaceableInterface => null;

        private void OnRegisterCaps(UUID agentId, Caps caps)
        {
            if (m_scene is null)
                return;

            if (!m_scene.UserManagementModule.IsLocalGridUser(agentId))
                return;

            caps.RegisterSimpleHandler("SetBadge", new SimpleStreamHandler($"/{UUID.Random()}", (req, resp) => HandleSetBadge(agentId, req, resp)));
        }

        private void HandleSetBadge(UUID agentId, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "POST")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (m_badgeModule is null)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            OSD body;
            try
            {
                body = OSDParser.DeserializeLLSDXml(httpRequest.InputStream);
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (body is not OSDMap requestMap)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            UUID targetId = agentId;
            if (requestMap.TryGetValue("target_id", out OSD targetOsd) && targetOsd.Type == OSDType.UUID)
                targetId = targetOsd.AsUUID();

            if (!requestMap.TryGetValue("customer_type", out OSD typeOsd)
                && !requestMap.TryGetValue("CustomerType", out typeOsd)
                && !requestMap.TryGetValue("badge", out typeOsd))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string desiredType = typeOsd.AsString() ?? string.Empty;

            if (!m_badgeModule.TrySetCustomerType(agentId, targetId, desiredType, out string message))
            {
                var error = new OSDMap
                {
                    ["status"] = OSD.FromInteger((int)HttpStatusCode.BadRequest),
                    ["reason"] = OSD.FromString(message ?? string.Empty)
                };

                httpResponse.ContentType = "application/llsd+xml";
                httpResponse.RawBuffer = OSDParser.SerializeLLSDXmlBytes(error);
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            var response = new OSDMap
            {
                ["status"] = OSD.FromInteger((int)HttpStatusCode.OK),
                ["customer_type"] = OSD.FromString(desiredType),
                ["target_id"] = OSD.FromUUID(targetId)
            };

            httpResponse.ContentType = "application/llsd+xml";
            httpResponse.RawBuffer = OSDParser.SerializeLLSDXmlBytes(response);
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }
    }
}

