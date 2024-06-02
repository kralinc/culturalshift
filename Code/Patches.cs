using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NeoModLoader.api;
using NeoModLoader.General;
using UnityEngine;
using ReflectionUtility;
using HarmonyLib;
using ai;
using ai.behaviours;

namespace CulturalShift 
{
	class Patches : MonoBehaviour
	{
		public static Harmony harmony = new Harmony("cd.mymod.wb.culturalshift");
		public static ModConfig conf = null;

		public static void init(ModConfig theConf) {
			conf = theConf;
		    harmony.Patch(
		        AccessTools.Method(typeof(DiplomacyHelpers), "startRebellion"),
		        prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), "startRebellion_Prefix"))
		    );
		    
		    harmony.Patch(
		        AccessTools.Method(typeof(City), "useInspire"),
		        prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), "useInspire_Prefix"))
		    );

			harmony.Patch(
				AccessTools.Method(typeof(Culture), "spreadAround"),
				prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), "spreadAround_Prefix"))
			);
		}
		
		public static bool startRebellion_Prefix(City pCity, Actor pActor, Plot pPlot) {
			Kingdom kingdom = pCity.kingdom;
			Kingdom kingdom2 = pCity.makeOwnKingdom();
			pCity.removeLeader();
			pCity.addNewUnit(pActor);
			City.makeLeader(pActor, pCity);
			War war = null;
			using ListPool<War> listPool = World.world.wars.getWars(kingdom);
			foreach (ref War item in listPool)
			{
				War current = item;
				if (current.main_attacker == kingdom && current.getAsset() == WarTypeLibrary.rebellion)
				{
					war = current;
					war.joinDefenders(kingdom2);
					break;
				}
			}
			if (war == null)
			{
				war = World.world.diplomacy.startWar(kingdom, kingdom2, WarTypeLibrary.rebellion);
				if (kingdom.hasAlliance())
				{
					foreach (Kingdom item2 in kingdom.getAlliance().kingdoms_hashset)
					{
						if (item2 != kingdom && item2.isOpinionTowardsKingdomGood(kingdom))
						{
							war.joinAttackers(item2);
						}
					}
				}
			}
			bool connectedChecked = false;
			bool isConnected = false;
			foreach (Actor supporter in pPlot.supporters)
			{
				City city = supporter.city;
				if (city != null && city.kingdom != kingdom2 && city.kingdom == kingdom)
				{
					if (!connectedChecked) {
						isConnected = city.isConnectedToCapital();
						connectedChecked = true;
					}
					city.joinAnotherKingdom(kingdom2);
				}
			}
			int count = kingdom.cities.Count;
			int maxCities = kingdom2.getMaxCities();
			maxCities -= kingdom2.cities.Count;
			if (maxCities < 0)
			{
				maxCities = 0;
			}
			if (maxCities > count / 3)
			{
				maxCities = (int)((float)count / 3f);
			}
			for (int i = 0; i < maxCities; i++)
			{
				if (!DiplomacyHelpers.checkMoreAlignedCities(kingdom2, kingdom))
				{
					break;
				}
			}
			makeNewCulture(kingdom2, kingdom, isConnected);
			return false;
		}
		
		public static void useInspire_Prefix(City __instance)
		{
		bool isConnected = __instance.isConnectedToCapital();
		Kingdom pAttacker = __instance.kingdom;
		__instance.makeOwnKingdom();
		World.world.diplomacy.startWar(pAttacker, __instance.kingdom, WarTypeLibrary.inspire, pLog: false);
		makeNewCulture(__instance.kingdom, pAttacker, isConnected);
		}

		public static bool spreadAround_Prefix(TileZone pZone, Culture __instance)
		{
			bool naturalTileSwapping = (bool) conf["CS"]["naturalTileSwapping"].GetValue();
			float naturalTileSwappingThreshold = (float) conf["CS"]["naturalTileSwappingThreshold"].GetValue();
			//just run the base method if this setting isn't on
			if (!naturalTileSwapping) {
				return true;
			}

			TileZone bestZoneToSpreadFrom = __instance.getBestZoneToSpreadFrom(pZone);
			Culture culture = pZone.culture;
			if (bestZoneToSpreadFrom == null)
			{
				return false;
			}
			List<Actor> list = new List<Actor>();
			if (bestZoneToSpreadFrom.culture == null)
			{
				__instance.spreadOn(bestZoneToSpreadFrom);
				return false;
			}
			int num = 0;
			for (int i = 0; i < bestZoneToSpreadFrom.neighbours.Count; i++)
			{
				if (bestZoneToSpreadFrom.neighbours[i].culture == culture)
				{
					num++;
				}
			}
			float num2 = 0f;
			list.Clear();
			Toolbox.fillListWithUnitsFromChunk(bestZoneToSpreadFrom.centerTile.chunk, list);
			int unitsWithOtherCulture = 0;
			for (int j = 0; j < list.Count; j++)
			{
				if (!(list[j].data.culture != __instance.data.id))
				{
					num2 += 0.05f;
				}else {
					unitsWithOtherCulture++;
				}
			}

			//Disable stealing tiles from cultures with less spread unless this culture has a proportion of units in the chunk greater than the threshold.
			if (naturalTileSwapping && (list.Count == 0 || (double) unitsWithOtherCulture / (double)list.Count < naturalTileSwappingThreshold)) {
				return false;
			}

			float num3 = __instance.stats.culture_spread_convert_chance.value * (float)num + num2;
			if (bestZoneToSpreadFrom.culture.followers > __instance.followers)
			{
				float num4 = (__instance.followers + 1) / (bestZoneToSpreadFrom.culture.followers + 1);
				num3 *= num4;
			}
			if (Toolbox.randomChance(num3))
			{
				__instance.spreadOn(bestZoneToSpreadFrom);
			}
			return false;
		}
		
		private static void makeNewCulture(Kingdom newKingdom, Kingdom oldKingdom, bool isConnected) {
			System.Random rand = new System.Random();
			City foundingCity = newKingdom.cities[0];
			Culture oldCulture = foundingCity.getCulture();

			float newCultureSameContinent = (float) conf["CS"]["newCultureSameContinent"].GetValue();
			float newCultureDiffContinent = (float) conf["CS"]["newCultureDiffContinent"].GetValue();
			bool newCultureByCapital = (bool) conf["CS"]["newCultureByCapital"].GetValue();
			bool removeCulture = (bool) conf["CS"]["removeCulture"].GetValue();
			bool mustMatchKingdom = (bool) conf["CS"]["mustMatchKingdom"].GetValue();

			double splitChance = (isConnected) ? newCultureSameContinent : newCultureDiffContinent;
			
			if (rand.NextDouble() < splitChance
			&& oldKingdom.cities.Count >= newKingdom.cities.Count
			&& (!mustMatchKingdom || foundingCity.data.culture == oldKingdom.cultureID)
			&& (newCultureByCapital || (oldKingdom.capital == null
			    || !City.nearbyBorders(oldKingdom.capital, foundingCity)))) {
				Culture newCulture = BehaviourActionBase<City>.world.cultures.newCulture(newKingdom.race, foundingCity);
				foreach (City city in newKingdom.cities) {
					if (city.getCulture().cities > 1 || removeCulture) {
						city.setCulture(newCulture);
						foreach (TileZone zone in city.zones) {
							newCulture.addZone(zone);
						}
						foreach (Actor citizen in city.units) {
							citizen.setCulture(newCulture);
						}
						foreach (Actor boat in city.boats) {
							boat.setCulture(newCulture);
						}
					}
				}

				updateTech(oldCulture.getCurrentLevel(), newCulture);
				
				string logMessage = "The new culture of " 
				+ newCulture.name
				+ " has diverged from "
				+ oldCulture.name;

				WorldLogMessage pMessage = new WorldLogMessage(logMessage);
				pMessage.city = foundingCity;
				pMessage.location = foundingCity.lastCityCenter;
				pMessage.add();
			}
		}

		private static void updateTech(int level, Culture newCulture) {
			while (newCulture.getCurrentLevel() < level) {
				newCulture.data.research_progress = 9999f;
				newCulture.updateProgress();
			}
		}
	}
}
