﻿using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using UnityEngine.Experimental.LowLevel;

namespace package.stormiumteam.shared
{
    public static class AppEvent<TEvent> where TEvent : IAppEvent
    {
        private static TEvent[] s_FixedEventList = new TEvent[0];
        public static bool s_IsObjEventsDirty = false;

        public static List<TEvent> DelayList = new List<TEvent>();
        public static List<TEvent> EventList = new List<TEvent>();
        public static List<TEvent> ObjList = new List<TEvent>();

        public static TEvent[] GetObjEvents()
        {
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

        protected override void OnCreateManager()
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

        protected override void OnDestroyManager()
        {
        }

        public void CheckDelayed<TEvent>()
            where TEvent : IAppEvent
        {
            foreach (var delayed in AppEvent<TEvent>.DelayList)
                SubscribeTo<TEvent, TEvent>(delayed);
            
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
                
            if (!HasRanAndPlayerLoopIsCorrect)
            {
                AppEvent<TSub>.DelayList.Add(obj);
                return;
            }

            if (AppEvent<TSub>.EventList.Contains(obj))
                return;   

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
                    var newManager = World.GetExistingManager(oldManager.GetType());
                    if (newManager != null && !newList.Contains(oldManager)) newList.Add((T) (object) newManager);

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
                    var newManager = World.GetExistingManager(oldManager.GetType());
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