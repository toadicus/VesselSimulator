﻿// Kerbal Engineer Redux
// Author:  CYBUTEK
// License: Attribution-NonCommercial-ShareAlike 3.0 Unported
//
// This class has taken a lot of inspiration from r4m0n's MuMech FuelFlowSimulator.  Although extremely
// similar to the code used within MechJeb, it is a clean re-write.  The similarities are a testiment
// to how well the MuMech code works and the robustness of the simulation algorithem used.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Engineer.Extensions;

namespace Engineer.VesselSimulator
{
    public class PartSim
    {
        public ResourceContainer resources = new ResourceContainer();
        public ResourceContainer resourceDrains = new ResourceContainer();
        ResourceContainer resourceFlowStates = new ResourceContainer();
        ResourceContainer resourceConsumptions = new ResourceContainer();

        Dictionary<int, bool> resourceCanSupply = new Dictionary<int, bool>();

        List<AttachNodeSim> attachNodes = new List<AttachNodeSim>();

        public Part part;              // This is only set while the data structures are being initialised
        public int partId = 0;
        public String name;
        public PartSim parent;
        public PartSim fuelLineTarget;
        public bool hasVessel;
        public bool isLanded;
        public int decoupledInStage;
        public int inverseStage;
        public int cost;
        double baseMass = 0d;
        double startMass = 0d;
        public double thrust = 0;
        public double actualThrust = 0;
        public double isp = 0;
        public String noCrossFeedNodeKey;
        public bool fuelCrossFeed;
        public bool isEngine;
        public bool isFuelLine;
        public bool isFuelTank;
        public bool isDecoupler;
        public bool isDockingNode;
        public bool isStrutOrFuelLine;
        public bool isSolidMotor;
        public bool isSepratron;
        public bool hasMultiModeEngine;
        public bool hasModuleEnginesFX;
        public bool hasModuleEngines;
        public bool localCorrectThrust;

        public PartSim(Part thePart, int id, double atmosphere)
        {
            part = thePart;
            partId = id;
            name = part.partInfo.name;
#if LOG
            MonoBehaviour.print("Create PartSim for " + name);
#endif
            parent = null;
            fuelCrossFeed = part.fuelCrossFeed;
            noCrossFeedNodeKey = part.NoCrossFeedNodeKey;
            decoupledInStage = DecoupledInStage(part);
            isDecoupler = IsDecoupler(part);
            isDockingNode = IsDockingNode();
            isFuelLine = part is FuelLine;
            isFuelTank = part is FuelTank;
            isStrutOrFuelLine = IsStrutOrFuelLine();
            isSolidMotor = IsSolidMotor();
            isSepratron = IsSepratron();
            inverseStage = part.inverseStage;
            //MonoBehaviour.print("inverseStage = " + inverseStage);

            cost = part.partInfo.cost;

            if (!part.Modules.Contains("LaunchClamp") && part.physicalSignificance == Part.PhysicalSignificance.FULL)
                baseMass = part.mass;

            foreach (PartResource resource in part.Resources)
            {
                // Make sure it isn't NaN as this messes up the part mass and hence most of the values
                // This can happen if a resource capacity is 0 and tweakable
                if (!Double.IsNaN(resource.amount))
                {
#if LOG
                    MonoBehaviour.print(resource.resourceName + " = " + resource.amount);
#endif
                    resources.Add(resource.info.id, resource.amount);
                    resourceFlowStates.Add(resource.info.id, resource.flowState ? 1 : 0);
                }
                else
                {
                    MonoBehaviour.print(resource.resourceName + " is NaN. Skipping.");
                }
            }

            startMass = GetMass();

            hasVessel = (part.vessel != null);
            isLanded = hasVessel && part.vessel.Landed;

            hasMultiModeEngine = part.HasModule<MultiModeEngine>();
            hasModuleEnginesFX = part.HasModule<ModuleEnginesFX>();
            hasModuleEngines = part.HasModule<ModuleEngines>();

            isEngine = hasMultiModeEngine || hasModuleEnginesFX || hasModuleEngines;

#if LOG
            MonoBehaviour.print("Created " + name + ". Decoupled in stage " + decoupledInStage);
#endif
        }

        public void CreateEngineSims(List<EngineSim> allEngines, double atmosphere)
        {
#if LOG
            LogMsg log = new LogMsg();
            log.buf.AppendLine("CreateEngineSims for " + name);

            foreach (PartModule partMod in part.Modules)
            {
                log.buf.AppendLine("Module: " + partMod.moduleName);
            }
#endif
            if (hasMultiModeEngine)
            {
                // A multi-mode engine has multiple ModuleEnginesFX but only one is active at any point
                // The mode of the engine is the engineID of the ModuleEnginesFX that is active
                string mode = part.GetModule<MultiModeEngine>().mode;

                foreach (ModuleEnginesFX engine in part.GetModules<ModuleEnginesFX>())
                {
                    if (engine.engineID == mode)
                    {
                        EngineSim engineSim = new EngineSim(this, atmosphere,
                                                            engine.maxThrust,
                                                            engine.thrustPercentage,
                                                            engine.requestedThrust,
                                                            engine.atmosphereCurve,
                                                            engine.throttleLocked,
                                                            engine.propellants);
                        allEngines.Add(engineSim);
                    }
                }
            }
            else
            {

                if (hasModuleEnginesFX)
                {
                    foreach (ModuleEnginesFX engine in part.GetModules<ModuleEnginesFX>())
                    {
                        EngineSim engineSim = new EngineSim(this, atmosphere,
                                                            engine.maxThrust,
                                                            engine.thrustPercentage,
                                                            engine.requestedThrust,
                                                            engine.atmosphereCurve,
                                                            engine.throttleLocked,
                                                            engine.propellants);
                        allEngines.Add(engineSim);
                    }
                }

                if (hasModuleEngines)
                {
                    foreach (ModuleEngines engine in part.GetModules<ModuleEngines>())
                    {
                        EngineSim engineSim = new EngineSim(this, atmosphere,
                                                            engine.maxThrust,
                                                            engine.thrustPercentage,
                                                            engine.requestedThrust,
                                                            engine.atmosphereCurve,
                                                            engine.throttleLocked,
                                                            engine.propellants);
                        allEngines.Add(engineSim);
                    }
                }
            }
#if LOG
            log.Flush();
#endif
        }


        public void SetupAttachNodes(Dictionary<Part, PartSim> partSimLookup)
        {
            attachNodes.Clear();
            foreach (AttachNode attachNode in part.attachNodes)
            {
                if (attachNode.attachedPart != null && attachNode.id != "Strut")
                {
                    PartSim attachedSim;
                    if (partSimLookup.TryGetValue(attachNode.attachedPart, out attachedSim))
                    {
#if LOG
                        MonoBehaviour.print("Adding attached node " + attachedSim.name + ":" + attachedSim.partId + "");
#endif
                        attachNodes.Add(new AttachNodeSim(attachedSim, attachNode.id, attachNode.nodeType));
                    }
                    else
                    {
                        MonoBehaviour.print("No PartSim for attached part (" + attachNode.attachedPart.partInfo.name + ")");
                    }
                }
            }

            if (isFuelLine)
            {
                if ((this.part as FuelLine).target != null)
                {
                    PartSim targetSim;
                    if (partSimLookup.TryGetValue((this.part as FuelLine).target, out targetSim))
                    {
#if LOG
                        MonoBehaviour.print("Fuel line target is " + targetSim.name + ":" + targetSim.partId + "");
#endif
                        fuelLineTarget = targetSim;
                    }
                }
                else
                {
                    fuelLineTarget = null;
                }
            }

            if (part.parent != null)
            {
                parent = null;
                if (!partSimLookup.TryGetValue(part.parent, out parent))
                {
                    MonoBehaviour.print("No PartSim for parent part (" + part.parent.partInfo.name + ")");
                }
            }
        }

        public int DecoupledInStage(Part thePart, int stage = -1)
        {
            if (IsDecoupler(thePart))
            {
                if (thePart.inverseStage > stage)
                {
                    stage = thePart.inverseStage;
                }
            }

            if (thePart.parent != null)
            {
                stage = DecoupledInStage(thePart.parent, stage);
            }

            return stage;
        }

        private bool IsDecoupler(Part thePart)
        {
            return thePart is Decoupler || thePart is RadialDecoupler || thePart.Modules.OfType<ModuleDecouple>().Count() > 0 || thePart.Modules.OfType<ModuleAnchoredDecoupler>().Count() > 0;
        }

        private bool IsDockingNode()
        {
            return part.Modules.OfType<ModuleDockingNode>().Count() > 0;
        }

        private bool IsStrutOrFuelLine()
        {
            return (part is StrutConnector || part is FuelLine) ? true : false;
        }

        private bool IsSolidMotor()
        {
            foreach (ModuleEngines engine in part.Modules.OfType<ModuleEngines>())
            {
                if (engine.throttleLocked)
                    return true;
            }

            return false;
        }

        private bool IsSepratron()
        {
            if (!part.ActivatesEvenIfDisconnected)
                return false;

            if (part is SolidRocket)
                return true;

            if (part.Modules.OfType<ModuleEngines>().Count() == 0)
                return false;

            if (part.Modules.OfType<ModuleEngines>().First().throttleLocked == true)
                return true;

            return false;
        }

        public void ReleasePart()
        {
            part = null;
        }


        // All functions below this point must not rely on the part member (it may be null)
        //

        public HashSet<PartSim> GetSourceSet(int type, List<PartSim> allParts, List<PartSim> allFuelLines, HashSet<PartSim> visited)
        {
#if LOG
            MonoBehaviour.print("GetSourceSet(" + ResourceContainer.GetResourceName(type) + ") for " + name + ":" + partId);
#endif
            HashSet<PartSim> allSources = new HashSet<PartSim>();
            HashSet<PartSim> partSources = new HashSet<PartSim>();

            // Rule 1: Each part can be only visited once, If it is visited for second time in particular search it returns empty list.
            if (visited.Contains(this))
            {
#if LOG
                MonoBehaviour.print("Returning empty set, already visited (" + name + ":" + partId + ")");
#endif
                return allSources;
            }

#if LOG
            MonoBehaviour.print("Adding this to visited");
#endif
            visited.Add(this);

            // Rule 2: Part performs scan on start of every fuel pipe ending in it. This scan is done in order in which pipes were installed. Then it makes an union of fuel tank sets each pipe scan returned. If the resulting list is not empty, it is returned as result.
            //MonoBehaviour.print("foreach fuel line");
            foreach (PartSim partSim in allFuelLines)
            {
                if (partSim.fuelLineTarget == this)
                {
#if LOG
                    MonoBehaviour.print("Adding fuel line as source (" + partSim.name + ":" + partSim.partId + ")");
#endif
                    partSources = partSim.GetSourceSet(type, allParts, allFuelLines, visited);
                    if (partSources.Count > 0)
                    {
                        allSources.UnionWith(partSources);
                        partSources.Clear();
                    }
                }
            }

            if (allSources.Count > 0)
            {
#if LOG
                MonoBehaviour.print("Returning " + allSources.Count + " fuel line sources (" + name + ":" + partId + ")");
#endif
                return allSources;
            }

            // Rule 3: If the part is not crossfeed capable, it returns empty list.
            //MonoBehaviour.print("Test crossfeed");
            if (!fuelCrossFeed)
            {
#if LOG
                MonoBehaviour.print("Returning empty set, no cross feed (" + name + ":" + partId + ")");
#endif
                return allSources;
            }

            // Rule 4: Part performs scan on each of its axially mounted neighbors. 
            //  Couplers (bicoupler, tricoupler, ...) are an exception, they only scan one attach point on the single attachment side, skip the points on the side where multiple points are. [Experiment]
            //  Again, the part creates union of scan lists from each of its neighbor and if it is not empty, returns this list. 
            //  The order in which mount points of a part are scanned appears to be fixed and defined by the part specification file. [Experiment]
            //MonoBehaviour.print("foreach attach node");
            foreach (AttachNodeSim attachSim in attachNodes)
            {
                if (attachSim.attachedPartSim != null)
                {
                    if (attachSim.nodeType == AttachNode.NodeType.Stack &&
                        (attachSim.attachedPartSim.fuelCrossFeed || attachSim.attachedPartSim.isFuelTank) &&
                        !(noCrossFeedNodeKey != null && noCrossFeedNodeKey.Length > 0 && attachSim.id.Contains(noCrossFeedNodeKey)))
                    {
#if LOG
                        MonoBehaviour.print("Adding attached part as source (" + attachSim.attachedPartSim.name + ":" + attachSim.attachedPartSim.partId + ")");
#endif
                        partSources = attachSim.attachedPartSim.GetSourceSet(type, allParts, allFuelLines, visited);
                        if (partSources.Count > 0)
                        {
                            allSources.UnionWith(partSources);
                            partSources.Clear();
                        }
                    }
                }
            }

            if (allSources.Count > 0)
            {
#if LOG
                MonoBehaviour.print("Returning " + allSources.Count + " attached sources (" + name + ":" + partId + ")");
#endif
                return allSources;
            }

            // Rule 5: If the part is fuel container for searched type of fuel (i.e. it has capability to contain that type of fuel and the fuel type was not disabled [Experiment]) and it contains fuel, it returns itself.
            // Rule 6: If the part is fuel container for searched type of fuel (i.e. it has capability to contain that type of fuel and the fuel type was not disabled) but it does not contain the requested fuel, it returns empty list. [Experiment]
#if LOG
            MonoBehaviour.print("testing enabled container");
#endif
            if (resources.HasType(type) && resourceFlowStates[type] != 0)
            {
                if (resources[type] > 1f)
                    allSources.Add(this);

#if LOG
                MonoBehaviour.print("Returning this as only source (" + name + ":" + partId + ")");
#endif
                return allSources;
            }

            // Rule 7: If the part is radially attached to another part and it is child of that part in the ship's tree structure, it scans its parent and returns whatever the parent scan returned. [Experiment] [Experiment]
            if (parent != null)
            {
                allSources = parent.GetSourceSet(type, allParts, allFuelLines, visited);
                if (allSources.Count > 0)
                {
#if LOG
                    MonoBehaviour.print("Returning " + allSources.Count + " parent sources (" + name + ":" + partId + ")");
#endif
                    return allSources;
                }
            }

            // Rule 8: If all preceding rules failed, part returns empty list.
#if LOG
            MonoBehaviour.print("Returning empty set, no sources found (" + name + ":" + partId + ")");
#endif
            return allSources;
        }


        public void RemoveAttachedParts(HashSet<PartSim> partSims)
        {
            // Loop through the attached parts
            foreach (AttachNodeSim attachSim in attachNodes)
            {
                // If the part is in the set then "remove" it by clearing the PartSim reference
                if (partSims.Contains(attachSim.attachedPartSim))
                    attachSim.attachedPartSim = null;
            }
        }


        public void DrainResources(double time)
        {
            //MonoBehaviour.print("DrainResources(" + name + ":" + partId + ", " + time + ")");
            foreach (int type in resourceDrains.Types)
            {
                //MonoBehaviour.print("draining " + (time * resourceDrains[type]) + " " + ResourceContainer.GetResourceName(type));
                resources.Add(type, -time * resourceDrains[type]);
                //MonoBehaviour.print(ResourceContainer.GetResourceName(type) + " left = " + resources[type]);
            }
        }

        public double TimeToDrainResource()
        {
            //MonoBehaviour.print("TimeToDrainResource(" + name + ":" + partId + ")");
            double time = double.MaxValue;

            foreach (int type in resourceDrains.Types)
            {
                //MonoBehaviour.print("type = " + ResourceContainer.GetResourceName(type) + "  amount = " + resources[type] + "  rate = " + resourceDrains[type]);
                if (resourceDrains[type] > 0)
                    time = Math.Min(time, resources[type] / resourceDrains[type]);
            }

            //if (time < double.MaxValue)
            //    MonoBehaviour.print("TimeToDrainResource(" + name + ":" + partId + ") = " + time);
            return time;
        }

        public double GetStartMass()
        {
            return startMass;
        }

        public double GetMass()
        {
            double mass = baseMass;

            foreach (int type in resources.Types)
                mass += resources.GetResourceMass(type);

            return mass;
        }

        public ResourceContainer Resources
        {
            get
            {
                return resources;
            }
        }

        public ResourceContainer ResourceConsumptions
        {
            get
            {
                return resourceConsumptions;
            }
        }

        public ResourceContainer ResourceDrains
        {
            get
            {
                return resourceDrains;
            }
        }

#if LOG
        public String DumpPartAndParentsToBuffer(StringBuilder buffer, String prefix)
        {
            if (parent != null)
            {
                prefix = parent.DumpPartAndParentsToBuffer(buffer, prefix) + " ";
            }

            DumpPartToBuffer(buffer, prefix);

            return prefix;
        }

        public void DumpPartToBuffer(StringBuilder buffer, String prefix, List<PartSim> allParts = null)
        {
            buffer.Append(prefix);
            buffer.Append(name);
            buffer.AppendFormat(":[id = {0:d}, decouple = {1:d}, invstage = {2:d}", partId, decoupledInStage, inverseStage);

            buffer.AppendFormat(", fuelCF = {0}", fuelCrossFeed);
            buffer.AppendFormat(", noCFNKey = '{0}'", noCrossFeedNodeKey);

            if (isFuelLine)
                buffer.AppendFormat(", fuelLineTarget = {0:d}", fuelLineTarget.partId);
            
            buffer.AppendFormat(", isSep = {0}", isSepratron);

            foreach (int type in resources.Types)
                buffer.AppendFormat(", {0} = {1:g6}", ResourceContainer.GetResourceName(type), resources[type]);

            if (attachNodes.Count > 0)
            {
                buffer.Append(", attached = <");
                attachNodes[0].DumpToBuffer(buffer);
                for (int i = 1; i < attachNodes.Count; i++)
                {
                    buffer.Append(", ");
                    attachNodes[i].DumpToBuffer(buffer);
                }
                buffer.Append(">");
            }

            // Add more info here

            buffer.Append("]\n");

            if (allParts != null)
            {
                String newPrefix = prefix + " ";
                foreach (PartSim partSim in allParts)
                {
                    if (partSim.parent == this)
                        partSim.DumpPartToBuffer(buffer, newPrefix, allParts);
                }
            }
        }
#endif
    }
}
