﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace package.stormiumteam.shared.modding
{
    public class CModInfo
    {
        [AttributeUsage(AttributeTargets.Field)]
        public class InjectAttribute : Attribute {}
        
        private Assembly[] m_Assemblies;

        public int Id { get; }

        public SModInfoData Data { get; }
        public string StreamingPath => Application.streamingAssetsPath + "\\" + NameId;

        public string DisplayName => Data.DisplayName;
        public string NameId => Data.NameId;

        public ReadOnlyCollection<Assembly> AttachedAssemblies
            => new ReadOnlyCollection<Assembly>(m_Assemblies);

        public CModInfo(SModInfoData data, int id)
        {
            Data = data;
            Id = id;

            if (NameId.Contains('/')
                || NameId.Contains('\\')
                || NameId.Contains('?')
                || NameId.Contains(':')
                || NameId.Contains('|')
                || NameId.Contains('*')
                || NameId.Contains('<')
                || NameId.Contains('>'))
            {
                throw new Exception($"Name id {NameId} got invalid characters");
            }
            
            // Create the path to the project...
            if (!Directory.Exists(StreamingPath))
            Directory.CreateDirectory(StreamingPath);
        }

        public static CModInfo CurrentMod
        {
            get
            {
                var assembly = Assembly.GetCallingAssembly();
                return World.Active.GetOrCreateManager<CModManager>().GetAssemblyMod(assembly);
            }
        }

        public static ModWorld CurrentModWorld
        {
            get
            {
                var assembly = Assembly.GetCallingAssembly();
                return World.Active.GetOrCreateManager<CModManager>().GetAssemblyMod(assembly).GetWorld();
            }
        }
    }

    public static class CModInfoExtensions
    {
        public static ModWorld GetWorld(this CModInfo modInfo)
        {
            return World.Active.GetOrCreateManager<CModManager>().GetModWorld(modInfo);
        }

        /* TODO: public static ModInputManager GetInputManager(this CModInfo modInfo)
        {
            return modInfo.GetWorld().GetOrCreateManager<ModInputManager>();
        }*/
    }
}