﻿using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder.GameObjectVerifier
{
    public class Plugin : IAssetBuilderVerifierPlugin
    {
        private static class GameObjectVerifier
        {
            private static readonly Tracer _tracer = Tracer.GetTracer(nameof(GameObjectVerifier), "Verifies that Game Objects are correct.");
            private static XmlNamespaceManager _namespaceManager;
            private static readonly Regex _transitionStateNameRegex = new Regex(@"CurDrawableSetTransitionAnimState\s*\(\""(?<arg>\w+)\""\)", RegexOptions.Compiled);

            private static bool VerifyModuleIds(XmlNode node, string gameObjectId, string srcFile)
            {
                bool result = true;
                XmlNodeList list = node.SelectNodes("ea:Behaviors/* | ea:Draws/* | ea:AI/* | ea:Body/*", _namespaceManager);
                Set<string> set = new Set<string>();
                foreach (XmlNode module in list)
                {
                    XmlAttribute attribute = module.Attributes["id"];
                    if (attribute is null || attribute.Value == string.Empty)
                    {
                        _tracer.TraceError("GameObject {0} has an unnamed module {1} in file {2}", gameObjectId, module.Name, srcFile);
                        result = false;
                    }
                    else
                    {
                        string lower = attribute.Value.ToLower();
                        if (set.Contains(lower))
                        {
                            _tracer.TraceError("GameObject {0} has 2 modules with the same name {1} in file {2}", gameObjectId, attribute.Value, srcFile);
                            result = false;
                        }
                        else
                        {
                            set.Add(lower);
                        }
                    }
                }
                return result;
            }

            private static bool VerifyDrawModules_DuplicateDefaultStates(XmlNode draw, string gameObjectId)
            {
                bool result = true;
                XmlNodeList list = draw.SelectNodes("ea:AnimationState[@ParseCondStateType=\"PARSE_DEFAULT\"]", _namespaceManager);
                if (list.Count > 1)
                {
                    _tracer.TraceWarning("GameObject {0} has {1} default animation states in draw element {2}", gameObjectId, list.Count, draw.Name);
                    result = false;
                }
                return result;
            }

            private static bool VerifyDrawModules_UniqueConditionsYes(XmlNode draw, string gameObjectId, Dictionary<string, int> stateNameAttributes)
            {
                bool result = true;
                XmlNodeList list = draw.SelectNodes("ea:AnimationState", _namespaceManager);
                Dictionary<string, int> dictionary = new Dictionary<string, int>();
                foreach (XmlNode node in list)
                {
                    XmlAttribute attribute = node.Attributes["ConditionsYes"];
                    if (!(attribute is null))
                    {
                        string value = attribute.Value;
                        if (!dictionary.ContainsKey(value))
                        {
                            string[] flags = value.Split(' ');
                            if (flags.Length == 0 || (flags.Length == 1 && flags[0] == "NONE"))
                            {
                                _tracer.TraceWarning("GameObject {0} has a PARSE_NORMAL without ConditionsYes set in draw element {1}", gameObjectId, draw.Name);
                                result = false;
                            }
                            bool hasDuplicates = false;
                            foreach (string other in dictionary.Keys)
                            {
                                string[] otherFlags = other.Split(' ');
                                if (flags.Length == otherFlags.Length)
                                {
                                    bool conditionsMatch = true;
                                    for (int idx = 0; idx < flags.Length; ++idx)
                                    {
                                        string flag = flags[idx];
                                        bool foundMatch = false;
                                        for (int idy = 0; idy < otherFlags.Length; ++idy)
                                        {
                                            if (flag.Equals(otherFlags[idy]))
                                            {
                                                foundMatch = true;
                                                break;
                                            }
                                        }
                                        if (!foundMatch)
                                        {
                                            conditionsMatch = false;
                                            break;
                                        }
                                    }
                                    if (conditionsMatch)
                                    {
                                        hasDuplicates = true;
                                    }
                                }
                            }
                            if (!hasDuplicates)
                            {
                                dictionary.Add(value, flags.Length);
                            }
                            else
                            {
                                _tracer.TraceWarning("GameObject {0} has duplicate ConditionYes entries for {1} in draw element {2}", gameObjectId, value, draw.Name);
                                result = false;
                            }
                        }
                        else
                        {
                            _tracer.TraceWarning("GameObject {0} has duplicate ConditionYes entries for {1} in draw element {2}", gameObjectId, value, draw.Name);
                            result = false;
                        }
                    }
                    attribute = node.Attributes["StateName"];
                    if (!(attribute is null))
                    {
                        string value = attribute.Value;
                        if (stateNameAttributes.ContainsKey(value))
                        {
                            stateNameAttributes[value] = stateNameAttributes[value] + 1;
                        }
                        else
                        {
                            stateNameAttributes.Add(value, 1);
                        }
                    }
                }
                return result;
            }

            private static bool VerifyDrawModules_StateNameReferences(XmlNode draw, Dictionary<string, int> stateNameAttributes, string gameObjectId)
            {
                bool result = true;
                XmlNodeList nodes = draw.SelectNodes("ea:AnimationState/ea:Script", _namespaceManager);
                foreach (XmlNode node in nodes)
                {
                    MatchCollection matches = _transitionStateNameRegex.Matches(node.FirstChild.Value);
                    foreach (Match match in matches)
                    {
                        if (stateNameAttributes.TryGetValue(match.Groups["arg"].Value, out int amount))
                        {
                            if (amount > 1)
                            {
                                _tracer.TraceWarning("GameObject {0} has a script with an ambiguous StateName reference to {1}, in Draw '{2}'",
                                                     gameObjectId,
                                                     match.Groups["arg"].Value,
                                                     draw.Name);
                                result = false;
                            }
                        }
                        else
                        {
                            _tracer.TraceWarning("GameObject {0} has a script with an invalid StateName reference to {1}, in Draw '{2}'",
                                                 gameObjectId,
                                                 match.Groups["arg"].Value,
                                                 draw.Name);
                            result = false;
                        }
                    }
                }
                return result;
            }

            private static bool VerifyDrawModules(XmlNode node, string gameObjectId)
            {
                bool result = true;
                foreach (XmlNode draw in node.SelectNodes("ea:Draws/*", _namespaceManager))
                {
                    result = VerifyDrawModules_DuplicateDefaultStates(draw, gameObjectId) && result;
                    Dictionary<string, int> stateNameAttributes = new Dictionary<string, int>();
                    result = VerifyDrawModules_UniqueConditionsYes(draw, gameObjectId, stateNameAttributes) && result;
                    result = VerifyDrawModules_StateNameReferences(draw, stateNameAttributes, gameObjectId) && result;
                }
                return result;
            }

            private static bool Verify(string gameObjectId, string srcFile, XmlNode node)
            {
                return VerifyModuleIds(node, gameObjectId, srcFile) && VerifyDrawModules(node, gameObjectId);
            }

            public static bool Verify(InstanceDeclaration instance)
            {
                _namespaceManager = instance.Document.NamespaceManager;
                return Verify(instance.Node.Attributes["id"].Value, instance.Document.SourcePath, instance.Node);
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
                case "GameObject":
                    return GameObjectVerifier.Verify(instance);
                default:
                    return true;
            }
        }
    }
}
