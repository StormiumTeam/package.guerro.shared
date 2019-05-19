﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.LowLevel;

namespace package.stormiumteam.shared
{
    public static class AppEvent<TEvent> where TEvent : IAppEvent
    {
        private static TEvent[] s_FixedEventList = new TEvent[0];
        private static bool s_IsObjEventsDirty = false;

        public static List<TEvent> DelayList = new List<TEvent>();
        public static List<TEvent> EventList = new List<TEvent>();
        public static List<TEvent> ObjList = new List<TEvent>();

        public static bool IsDirty => s_IsObjEventsDirty;

        public static TEvent[] GetObjEvents()
        {
            // if we still have some delayed subscribers, try to force an update to the appEventSystem
            if (DelayList.Count > 0)
            {
                var appEventSystem = World.Active.GetOrCreateSystem<AppEventSystem>();
                appEventSystem.ForceUpdate();
            }
                
            if (!s_IsObjEventsDirty) return s_FixedEventList;

            s_IsObjEventsDirty = false;
            
            if (s_FixedEventList.Length != EventList.Count)
            {
                s_FixedEventList = EventList.ToArray();
                return s_FixedEventList;
            }

            for (int i = 0; i != s_FixedEventList.Length; i++)
            {
                s_FixedEventList[i] = EventList[i];
            }

            return s_FixedEventList;
        }

        public static void MakeDirtyObjEvents()
        {
            s_IsObjEventsDirty = true;
        }
    }
        
    public interface IAppEvent
    {
            
    }
    
    // Urgent: This should be remade as the playerloop don't have the system in order (because of the new ComponentSystemGroup)
    public class AppEventSystem : ComponentSystem
    {
        private bool m_HasRan;
        private static List<Type> m_AllExecutables = new List<Type>();
        
        private static MethodInfo m_RemakeMethod;
        private static MethodInfo m_CheckDelayed;

        public static void Register<TEvent>()
            where TEvent : IAppEvent
        {
            m_AllExecutables.Add(typeof(TEvent));
        }

        internal bool HasRanAndPlayerLoopIsCorrect =>
            m_HasRan
            && ScriptBehaviourUpdateOrder.CurrentPlayerLoop.subSystemList != null
            && ScriptBehaviourUpdateOrder.CurrentPlayerLoop.subSystemList.Length > 0
            && ECSWorldLoop.LoopableWorlds.Contains(World);

        private long m_LoopVersion;

        protected override void OnCreate()
        {
            m_HasRan = false;

            m_RemakeMethod = typeof(AppEventSystem).GetMethod(nameof(Remake));
            m_CheckDelayed = typeof(AppEventSystem).GetMethod(nameof(CheckDelayed));
        }

        public void CheckLoopValidity()
        {
            if (ECSWorldLoop.Version != m_LoopVersion)
            {
                m_LoopVersion = ECSWorldLoop.Version;

                foreach (var executable in m_AllExecutables)
                {
                    var generic = m_RemakeMethod.MakeGenericMethod(executable);
                    generic.Invoke(this, new object[] {});
                }
            }
        }

        protected override void OnStartRunning()
        {
        }

        internal void ForceUpdate()
        {
            OnUpdate();
        }
        
        protected override void OnUpdate()
        {
            // Run delayed actions
            if (!m_HasRan && ScriptBehaviourUpdateOrder.CurrentPlayerLoop.subSystemList != null
                          && ScriptBehaviourUpdateOrder.CurrentPlayerLoop.subSystemList.Length > 0
                          && (ECSWorldLoop.LoopableWorlds.Contains(World) || World == World.Active))
            {
                m_HasRan = true;

                foreach (var executable in m_AllExecutables)
                {
                    var generic = m_CheckDelayed.MakeGenericMethod(executable);
                    generic.Invoke(this, new object[] {});
                }
            }
        }

        protected override void OnDestroy()
        {
        }

        public void CheckDelayed<TEvent>()
            where TEvent : IAppEvent
        {
            var currDelayed = AppEvent<TEvent>.DelayList.ToList();
            foreach (var delayed in currDelayed)
            {
                Debug.LogError($"Existing {delayed}");
                SubscribeTo<TEvent, TEvent>(delayed);
            }

            if (currDelayed.Count != AppEvent<TEvent>.DelayList.Count)
            {
                for (var i = currDelayed.Count - 1; i != AppEvent<TEvent>.DelayList.Count; i++)
                {
                    Debug.LogError($"{AppEvent<TEvent>.DelayList[i]} got added.");
                }
            }
            
            AppEvent<TEvent>.DelayList.Clear();
        }

        public void SubscribeToAll<TObj>(TObj obj)
        {
            var interfaces = obj.GetType().GetInterfaces();
            foreach (var @interface in interfaces)
            {
                if (!typeof(IAppEvent).IsAssignableFrom(@interface)) continue;
                
                var method  = typeof(AppEventSystem).GetMethod("SubscribeTo");
                var generic = method.MakeGenericMethod(@interface, @interface);
                generic.Invoke(this, new object[] {obj});
            }
        }

        public void SubscribeTo<TSub, TObj>(TObj __obj)
            where TSub : IAppEvent
        {
            var obj = (TSub)(object) __obj;

            if (!m_AllExecutables.Contains(typeof(TSub)))
            {
                Register<TSub>();
            }
                
            if (!HasRanAndPlayerLoopIsCorrect && !AppEvent<TSub>.DelayList.Contains(obj))
            {
                AppEvent<TSub>.DelayList.Add(obj);
                
                return;
            }

            if (AppEvent<TSub>.EventList.Contains(obj))
            {
                return;
            }

            var oldList = AppEvent<TSub>.EventList;
            var newList = new List<TSub>();

            var currPlayerLoop = ScriptBehaviourUpdateOrder.CurrentPlayerLoop;
            AddSystem(currPlayerLoop, obj, oldList, newList);

            oldList.Clear();
            AppEvent<TSub>.EventList = newList;
            AppEvent<TSub>.ObjList.Add(obj);

            foreach (var subObj in AppEvent<TSub>.ObjList)
            {
                if (!AppEvent<TSub>.EventList.Contains(subObj))
                    AppEvent<TSub>.EventList.Add(subObj);
            }
            
            AppEvent<TSub>.MakeDirtyObjEvents();
        }
        
        public void UnsubcribeFromAll<TObj>(TObj obj)
        {
            var interfaces = obj.GetType().GetInterfaces();
            foreach (var @interface in interfaces)
            {
                if (!typeof(IAppEvent).IsAssignableFrom(@interface)) continue;
                
                var method  = typeof(AppEventSystem).GetMethod("UnsubscribeFrom");
                var generic = method.MakeGenericMethod(@interface);
                generic.Invoke(this, new object[] {obj});
            }
        }
        
        public void UnsubscribeFrom<TSub>(TSub obj)
            where TSub : IAppEvent
        {
            if (AppEvent<TSub>.ObjList.Contains(obj))
                AppEvent<TSub>.ObjList.Remove(obj);
            
            if (AppEvent<TSub>.EventList.Contains(obj))
                AppEvent<TSub>.EventList.Remove(obj);
            
            AppEvent<TSub>.MakeDirtyObjEvents();
        }

        public void Remake<TSub>()
            where TSub : IAppEvent
        {
            var oldList = AppEvent<TSub>.EventList;
            var newList = new List<TSub>();

            var currPlayerLoop = ScriptBehaviourUpdateOrder.CurrentPlayerLoop;
            RemakeSystemLoop(currPlayerLoop, oldList, newList);

            oldList.Clear();
            AppEvent<TSub>.EventList = newList;
            
            foreach (var subObj in AppEvent<TSub>.ObjList)
            {
                if (!AppEvent<TSub>.EventList.Contains(subObj))
                    AppEvent<TSub>.EventList.Add(subObj);
            }
            
            AppEvent<TSub>.MakeDirtyObjEvents();
        }
        
        // ...
        private void AddSystem<T>(PlayerLoopSystem loopSystem, T wantedType, List<T> oldList, List<T> newList)
        {
            foreach (var oldManager in oldList)
            {
                if (loopSystem.type == oldManager.GetType())
                {
                    var newManager = World.GetExistingSystem(oldManager.GetType());
                    if (newManager != null && !newList.Contains(oldManager))
                    {
                        newList.Add((T) (object) newManager);
                    }

                    goto phase2;
                }
            }

            if (!newList.Contains(wantedType) && loopSystem.type == wantedType.GetType())
            {
                newList.Add(wantedType);
            }

            phase2:
            {
                if (loopSystem.subSystemList != null)
                {
                    foreach (var innerLoopSystem in loopSystem.subSystemList)
                        AddSystem(innerLoopSystem, wantedType, oldList, newList);
                }
            }
        }
        
        private void RemakeSystemLoop<T>(PlayerLoopSystem loopSystem, List<T> oldList, List<T> newList)
        {
            foreach (var oldManager in oldList)
            {
                if (loopSystem.type == oldManager.GetType())
                {
                    var newManager = World.GetExistingSystem(oldManager.GetType());
                    if (newManager != null && !newList.Contains(oldManager)) newList.Add((T) (object) newManager);

                    goto phase2;
                }
            }

            phase2:
            {
                if (loopSystem.subSystemList != null)
                {
                    foreach (var innerLoopSystem in loopSystem.subSystemList)
                        RemakeSystemLoop(innerLoopSystem, oldList, newList);
                }
            }
        }
    }
}