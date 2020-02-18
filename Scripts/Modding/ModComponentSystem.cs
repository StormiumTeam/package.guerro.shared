﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using JetBrains.Annotations;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.shared.modding
{
	public abstract class ModComponentSystem : ComponentSystem
	{
		public ModWorld ModWorld;

		internal void AddManagerForMod(ModWorld modWorld)
		{
			ModWorld = modWorld;
		}
	}

	public class ModWorld
	{
		[NotNull]
		private static readonly MethodInfo s_DestroyInstance;

		[NotNull]
		private static readonly MethodInfo s_CreateInstance;

		private static readonly FastDictionary<int, ModWorld> s_WorldsModIdLookup =
			new FastDictionary<int, ModWorld>();

		internal static readonly List<ModWorld> AllWorlds         = new List<ModWorld>();
		private                  bool           m_AllowGetManager = true;

		//@TODO: What about multiple managers of the same type...
		private Dictionary<Type, ModComponentSystem> m_BehaviourManagerLookup =
			new Dictionary<Type, ModComponentSystem>();

		private List<ModComponentSystem> m_BehaviourManagers = new List<ModComponentSystem>();

		private int m_DefaultCapacity = 10;

		static ModWorld()
		{
			// ReSharper disable AssignNullToNotNullAttribute
			s_DestroyInstance = typeof(ComponentSystemBase)
				.GetMethod("DestroyInstance", BindingFlags.NonPublic
				                              | BindingFlags.Instance);
			s_CreateInstance = typeof(ComponentSystemBase)
				.GetMethod("CreateInstance", BindingFlags.NonPublic
				                             | BindingFlags.Instance);

			Debug.Assert(s_DestroyInstance != null, "s_DestroyInstance == null");
			Debug.Assert(s_CreateInstance != null, "s_CreateInstance == null");
			// ReSharper restore AssignNullToNotNullAttribute
		}

		public ModWorld(CModInfo modInfo)
		{
			Mod = modInfo;

			s_WorldsModIdLookup[Mod.Id] = this;
			AllWorlds.Add(this);
		}

		// Well, it somewhat copy the same code of Entity/World.cs
		public CModInfo Mod { get; }

		public IEnumerable<ModComponentSystem> BehaviourManagers => new ReadOnlyCollection<ModComponentSystem>(m_BehaviourManagers);

		public int Version { get; private set; }

		public bool IsCreated => m_BehaviourManagers != null;

		private int GetCapacityForType(Type type)
		{
			return m_DefaultCapacity;
		}

		public void SetDefaultCapacity(int value)
		{
			m_DefaultCapacity = value;
		}

		public static ModWorld GetOrCreate(CModInfo modInfo)
		{
			if (s_WorldsModIdLookup.TryGetValue(modInfo.Id, out var world)) return world;

			return new ModWorld(modInfo);
		}

		public static void DisposeAll()
		{
			while (AllWorlds.Count != 0)
				AllWorlds[0].Dispose();
		}

		public void Dispose()
		{
			if (!IsCreated)
				throw new ArgumentException("World is already disposed");

			if (AllWorlds.Contains(this))
				AllWorlds.Remove(this);

			// Destruction should happen in reverse order to construction
			m_BehaviourManagers.Reverse();

			m_AllowGetManager = false;
			foreach (var behaviourManager in m_BehaviourManagers)
				try
				{
					DestroyManager(behaviourManager);
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}

			m_BehaviourManagers.Clear();
			m_BehaviourManagerLookup.Clear();

			m_BehaviourManagers      = null;
			m_BehaviourManagerLookup = null;
		}

		//
		// Internal
		//
		private ModComponentSystem CreateManagerInternal(Type type, int capacity, object[] constructorArguments)
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			if (!m_AllowGetManager)
				throw new ArgumentException("During destruction of a system you are not allowed to create more systems.");

			if (constructorArguments != null && constructorArguments.Length != 0)
			{
				var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (constructors.Length == 1 && constructors[0].IsPrivate)
					throw new MissingMethodException($"Constructing {type} failed because the constructor was private, it must be public.");
			}
#endif

			m_AllowGetManager = true;
			ModComponentSystem manager;
			try
			{
				manager = Activator.CreateInstance(type, constructorArguments) as ModComponentSystem;
			}
			catch
			{
				m_AllowGetManager = false;
				throw;
			}

			m_BehaviourManagers.Add(manager);
			AddTypeLookup(type, manager);

			try
			{
				manager.AddManagerForMod(this);
				CreateManagerExtraInternal(manager, capacity);
			}
			catch
			{
				RemoveManagerInteral(manager);
				throw;
			}

			++Version;
			return manager;
		}

		private ModComponentSystem GetExistingSystemInternal(Type type)
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			if (!IsCreated)
				throw new ArgumentException("During destruction ");
			if (!m_AllowGetManager)
				throw new ArgumentException("During destruction of a system you are not allowed to get or create more systems.");
#endif

			ModComponentSystem manager;
			if (m_BehaviourManagerLookup.TryGetValue(type, out manager))
				return manager;

			return null;
		}

		private ModComponentSystem GetOrCreateSystemInternal(Type type)
		{
			var manager = GetExistingSystemInternal(type);

			return manager ?? CreateManagerInternal(type, GetCapacityForType(type), null);
		}

		private void AddTypeLookup(Type type, ModComponentSystem manager)
		{
			while (type != typeof(ComponentSystemBase))
			{
				if (!m_BehaviourManagerLookup.ContainsKey(type))
					m_BehaviourManagerLookup.Add(type, manager);

				type = type.BaseType;
			}
		}

		private void RemoveManagerInteral(ModComponentSystem manager)
		{
			if (!m_BehaviourManagers.Remove(manager))
				throw new ArgumentException("manager does not exist in the world");
			++Version;

			var type = manager.GetType();
			while (type != typeof(ComponentSystemBase))
			{
				if (m_BehaviourManagerLookup[type] == manager)
				{
					m_BehaviourManagerLookup.Remove(type);

					foreach (var otherManager in m_BehaviourManagers)
						if (otherManager.GetType().IsSubclassOf(type))
							AddTypeLookup(otherManager.GetType(), otherManager);
				}

				type = type.BaseType;
			}
		}

		//
		// Extra internal
		//
		private void RemoveManagerExtraInternal(ModComponentSystem manager)
		{
			s_DestroyInstance.Invoke(manager, null);
		}

		private void CreateManagerExtraInternal(ModComponentSystem manager, int capacity)
		{
			s_CreateInstance.Invoke(manager, new object[] {World.DefaultGameObjectInjectionWorld, capacity});
		}

		//
		// Public
		//
		public ModComponentSystem CreateManager(Type type, params object[] constructorArgumnents)
		{
			return CreateManagerInternal(type, GetCapacityForType(type), constructorArgumnents);
		}

		public T CreateManager<T>(params object[] constructorArgumnents) where T : ModComponentSystem
		{
			return (T) CreateManagerInternal(typeof(T), GetCapacityForType(typeof(T)), constructorArgumnents);
		}

		public T GetOrCreateSystem<T>() where T : ModComponentSystem
		{
			return (T) GetOrCreateSystemInternal(typeof(T));
		}

		public ModComponentSystem GetOrCreateSystem(Type type)
		{
			return GetOrCreateSystemInternal(type);
		}

		public T GetExistingSystem<T>() where T : ModComponentSystem
		{
			return (T) GetExistingSystemInternal(typeof(T));
		}

		public ModComponentSystem GetExistingSystem(Type type)
		{
			return GetExistingSystemInternal(type);
		}

		public void DestroyManager(ModComponentSystem manager)
		{
			RemoveManagerInteral(manager);
			RemoveManagerExtraInternal(manager);
		}
	}
}