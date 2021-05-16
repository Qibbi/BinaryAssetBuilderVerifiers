using BinaryAssetBuilder.Core;
using System.Collections.Generic;
using System.Xml;

namespace BinaryAssetBuilder.HierarchyVerifier
{
    public class Plugin : IAssetBuilderVerifierPlugin
    {
        private static class HierarchyVerifier
        {
            private static readonly Tracer _tracer = Tracer.GetTracer(nameof(HierarchyVerifier), "Verifies that there are no hash collisions in bones of a hierarchy.");

            public static bool Verify(InstanceDeclaration instance)
            {
                bool result = true;
                XmlNodeList list = instance.Node.SelectNodes("ea:Pivot", instance.Document.NamespaceManager);
                Dictionary<uint, string> hashMap = new Dictionary<uint, string>();
                foreach (XmlNode pivot in list)
                {
                    string name = pivot.Attributes["Name"]?.Value;
                    if (string.IsNullOrEmpty(name))
                    {
                        _tracer.TraceError("W3DHierarchy {0} has a pivot without a name.", instance.Node.Attributes["id"].Value);
                        result = false;
                    }
                    uint hash = HashProvider.GetCaseInsensitiveSymbolHash(name);
                    if (hashMap.ContainsKey(hash))
                    {
                        _tracer.TraceError("W3DHierarchy {0} has two pivots ('{1}' and '{2}') sharing a common hash", instance.Node.Attributes["id"].Value, hashMap[hash], name);
                        result = false;
                    }
                    else
                    {
                        hashMap.Add(hash, name);
                    }
                }
                return result;
            }
        }

        public void Initialize(object configObject, TargetPlatform platform)
        {
        }

        public bool VerifyInstance(InstanceDeclaration instance)
        {
            if (instance.Node is null)
            {
                return true;
            }
            switch (instance.Node.Name)
            {
                case "W3DHierarchy":
                    return HierarchyVerifier.Verify(instance);
                default:
                    return true;
            }
        }
    }
}
