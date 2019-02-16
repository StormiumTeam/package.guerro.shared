﻿using Unity.Entities;

namespace package.stormiumteam.shared
{
    public static class FastEntitiesExtensions
    {
        public static bool HasComponent<T>(this Entity entity, World world = null)
        {
            if (world == null)
                world = World.Active;
            return world.GetExistingManager<EntityManager>().HasComponent<T>(entity);
        }

        public static T GetComponentData<T>(this Entity entity, World world = null)
            where T : struct, IComponentData
        {
            if (world == null)
                world = World.Active;
            return world.GetExistingManager<EntityManager>().GetComponentData<T>(entity);
        }

        public static void SetComponentData<T>(this Entity entity, T data, World world = null)
            where T : struct, IComponentData
        {
            if (world == null)
                world = World.Active;
            world.GetExistingManager<EntityManager>().SetComponentData(entity, data);
        }

        public static void SetOrAddComponentData<T>(this Entity entity, T data, World world = null)
            where T : struct, IComponentData
        {
            if (world == null)
                world = World.Active;
            var entityManager = world.GetExistingManager<EntityManager>();
            var hasComponent = entityManager.HasComponent<T>(entity);
            if (hasComponent && !ComponentType.Create<T>().IsZeroSized)
                entityManager.SetComponentData(entity, data);
            else if (!hasComponent)
                entityManager.AddComponentData(entity, data);
        }

        public static void RemoveComponentIfExist<T>(this Entity entity, World world = null)
            where T : struct
        {
            if (world == null)
                world = World.Active;
            var entityManager = world.GetExistingManager<EntityManager>();
            if (entityManager.HasComponent<T>(entity))
                entityManager.RemoveComponent<T>(entity);
        }

        public static void SetOrAddSharedComponentData<T>(this Entity entity, T data, World world = null)
            where T : struct, ISharedComponentData
        {
            if (world == null)
                world = World.Active;
            var entityManager = world.GetExistingManager<EntityManager>();
            if (entityManager.HasComponent<T>(entity))
                entityManager.SetSharedComponentData(entity, data);
            else
                entityManager.AddSharedComponentData(entity, data);
        }
    }
}