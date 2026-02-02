using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;

namespace Services
{
	[CreateAssetMenu(fileName = "Platform Error Data_ New", menuName = "Data/Platform Error Data")]
	public class PlatformErrorData : ScriptableObject
	{
		[Serializable]
		public class PlatformErrorSet
		{
			public string platformName;
			public List<PlatformErrorEntry> errors;
			public PlatformErrorEntry fallback;
		}

		[Serializable]
		public class PlatformErrorEntry
		{
			public string matchString;
			public string errorCode;
			public LocalizedString localizedError;
		}

		public List<PlatformErrorSet> platforms;

		public PlatformErrorEntry fallback;

		public PlatformErrorEntry GetEntry(string platform, string rawError)
		{
			var platformSet = platforms.FirstOrDefault(p => p.platformName == platform);
			if (platformSet == null)
			{
				Debug.LogWarning($"[Platform Error] Unmapped error for platform '{platform}': {rawError}");
				return fallback;
			}

			foreach (var entry in platformSet.errors)
			{
				if (rawError.Contains(entry.matchString))
					return entry;
			}

			Debug.LogWarning($"[Platform Error] Unmapped error for platform '{platform}': {rawError}");
			return platformSet.fallback ?? fallback;
		}
	}
}