﻿using Unity.Entities;

namespace Scripts.Utilities
{
    public static class ReplicationInstanceExtension
    {
        public static int GetId(this IReplicationInstance instance)
        {
            var world = World.Active;
            var mgr = world.GetExistingSystem<ReplicationInstanceManager>();
            
            return mgr.GetId(instance);
        }
    }
}