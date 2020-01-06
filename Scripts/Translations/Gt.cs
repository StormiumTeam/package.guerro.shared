using System;
using System.Collections.Generic;
using System.IO;
using Karambolo.PO;
using Unity.Entities;
using UnityEngine;

namespace StormiumTeam.Shared
{
	public class LocalizationSystem : ComponentSystem
	{
		private Language       m_CurrentLanguage;
		private List<Language> m_Languages;

		private Dictionary<string, Localization> m_LocalizationFromName;

		public Language Current
		{
			get => m_CurrentLanguage;
			set
			{
				foreach (var local in m_LocalizationFromName) local.Value.SetLanguage(value);

				m_CurrentLanguage = value;
			}
		}

		protected override void OnCreate()
		{
			m_LocalizationFromName = new Dictionary<string, Localization>(8);
			m_Languages = new List<Language>(16)
			{
				new Language("en", "English") {IsDefault = true},
				new Language("fr", "Français"),
				new Language("pl", "Polish"),
			};
			Current = m_Languages[2];
		}

		protected override void OnUpdate()
		{
		}

		public Localization LoadLocal(string name)
		{
			if (!m_LocalizationFromName.TryGetValue(name, out var localization))
			{
				localization = new Localization(m_Languages, name);
				localization.SetLanguage(Current);

				m_LocalizationFromName[name] = localization;
			}

			return localization;
		}

		public class Language
		{
			public string Id;
			public bool   IsDefault;
			public string Name;

			public Language(string id, string name)
			{
				Id   = id;
				Name = name;
			}
		}
	}

	public sealed class Localization
	{
		private LocalizedString             m_CurrentTable;
		private LocalizationSystem.Language m_Language;

		private readonly Dictionary<string, LocalizedString> m_LocalizedStrings;

		public Localization(List<LocalizationSystem.Language> languages, string name, string customPath = null)
		{
			m_LocalizedStrings = new Dictionary<string, LocalizedString>(languages.Count);

			if (string.IsNullOrEmpty(customPath))
				customPath = Application.streamingAssetsPath + "/local/";

			// {0} = language, {1} = name
			var                         pathResult = $"{customPath}/{{0}}/{{1}}.po";
			LocalizationSystem.Language defLang    = null;
			foreach (var lang in languages)
			{
				POParseResult result;

				var parser = new POParser();
				using (var stream = File.OpenText(string.Format(pathResult, lang.Id, name)))
				{
					result = parser.Parse(stream);
				}

				result.Catalog.Language = lang.Id;
				m_LocalizedStrings[lang.Id] = new LocalizedString
				{
					LanguageId = lang.Id,
					Catalog    = result.Catalog
				};

				if (lang.IsDefault && defLang == null)
					defLang = lang;
				else if (lang.IsDefault) throw new Exception("There is already a default language!");
			}

			if (defLang == null)
				return;

			// Patch non-default languages...
			foreach (var localized in m_LocalizedStrings)
			{
				if (localized.Key == defLang.Id)
					continue;

				var catalog = localized.Value.Catalog;
				foreach (var defLocal in m_LocalizedStrings[defLang.Id].Catalog)
				{
					if (catalog.TryGetValue(defLocal.Key, out var _))
						continue;
					catalog.Add(defLocal);
				}
			}
		}

		public string Name { get; private set; }

		public string this[string id, string context = null, string plural = null] => _(id, context, plural);

		internal void SetLanguage(LocalizationSystem.Language language)
		{
			m_Language = language;

			m_CurrentTable = m_LocalizedStrings[m_Language.Id];
		}

		public string _(string id, string context = null, string plural = null)
		{
			if (m_Language == null)
				throw new InvalidOperationException("No current language defined...");

			return m_CurrentTable.Catalog.GetTranslation(new POKey(id, plural, context));
		}

		public class LocalizedString
		{
			public POCatalog Catalog;
			public string    LanguageId;
		}
	}
}