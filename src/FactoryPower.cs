using HarmonyLib;
using UnityEngine;

namespace SprayFactory
{
    internal static class FactoryPower
    {
        internal static float Ratio(PowerSystem system,int consumerId)
        {
            if(system==null || consumerId<=0 || consumerId>=system.consumerPool.Length || system.consumerPool[consumerId].id!=consumerId) return 0;
            int network=system.consumerPool[consumerId].networkId;
            return network>0 && network<system.netPool.Length && system.netPool[network]!=null
                ?Mathf.Clamp01((float)system.netPool[network].consumerRatio):0;
        }

        internal static void Restore(PlanetFactory factory)
        {
            var traffic=factory.cargoTraffic;var system=factory.powerSystem;
            if(traffic==null || system==null) return;
            for(int i=1;i<traffic.spraycoaterCursor;i++)
            {
                ref var component=ref traffic.spraycoaterPool[i];
                if(component.id!=i || !FactoryRuntime.IsFactory(factory,component.entityId)) continue;
                int consumerId=factory.entityPool[component.entityId].powerConId;
                if(consumerId<=0 || consumerId>=system.consumerPool.Length || system.consumerPool[consumerId].id!=consumerId
                    || system.consumerPool[consumerId].entityId!=component.entityId)
                    consumerId=system.NewConsumerComponent(component.entityId,Prototypes.WorkEnergyPerTick,Prototypes.IdleEnergyPerTick);
                component.pcId=consumerId;
                ref var consumer=ref system.consumerPool[consumerId];
                consumer.workEnergyPerTick=Prototypes.WorkEnergyPerTick;consumer.idleEnergyPerTick=Prototypes.IdleEnergyPerTick;
                consumer.SetRequiredEnergy(false);
            }
        }
    }

    [HarmonyPatch(typeof(PlanetFactory),nameof(PlanetFactory.Import))]
    internal static class RestoreFactoryPower
    {
        private static void Postfix(PlanetFactory __instance) { FactoryPower.Restore(__instance); }
    }
}
