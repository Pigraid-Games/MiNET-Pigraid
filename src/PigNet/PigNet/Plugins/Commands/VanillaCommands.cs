﻿#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/PigNet/blob/master/LICENSE. 
// The License is based on the Mozilla Public License Version 1.1, but Sections 14 
// and 15 have been added to cover use of software over a computer network and 
// provide for limited attribution for the Original Developer. In addition, Exhibit A has 
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is PigNet.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2018 Niclas Olofsson. 
// All Rights Reserved.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using PigNet.Entities;
using PigNet.Entities.Hostile;
using PigNet.Entities.Passive;
using PigNet.Entities.Vehicles;
using PigNet.Entities.World;
using PigNet.Items;
using PigNet.Items.Custom;
using PigNet.Items.Weapons;
using PigNet.Net;
using PigNet.Net.EnumerationsTable;
using PigNet.Net.Packets.Mcpe;
using PigNet.Plugins.Attributes;
using PigNet.UI;
using PigNet.Utils;
using PigNet.Utils.Skins;
using PigNet.Utils.Vectors;
using PigNet.Worlds;

namespace PigNet.Plugins.Commands;

public class VanillaCommands
{
	
	public enum DayNight
	{
		Day = 1000,
		Night = 13000
	}

	public enum fogMode
	{
		remove, push
	}

	private static readonly ILog Log = LogManager.GetLogger(typeof(VanillaCommands));

	[Command(Name = "IsDead")]
	[Authorize(Permission = 4)]
	public void IsDead(Player commander, Target target)
	{
		Player pTarget = target.Players.FirstOrDefault();
		if (pTarget == null)
		{
			commander.SendMessage("Couldn't find a player to hide");
			return;
		}
		
		commander.SendMessage($"IsDead: {pTarget.HealthManager.IsDead}");
	}
	

	[Command(Name = "Hide")]
	[Authorize(Permission = 4)]
	public void HidePlayer(Player commander, Target target)
	{
		Player pTarget = target.Players.FirstOrDefault();
		if (pTarget == null)
		{
			commander.SendMessage("Couldn't find a player to hide");
			return;
		}
		commander.HidePlayer(pTarget);
	}

	[Command(Name = "Unhide")]
	[Authorize(Permission = 4)]
	public void UnhidePlayer(Player commander, Target target)
	{
		Player pTarget = target.Players.FirstOrDefault();
		if (pTarget == null)
		{
			commander.SendMessage("Couldn't find a player to hide");
			return;
		}
		commander.ShowPlayer(pTarget);
	}

	[Command(Name = "CustomForm")]
	[Authorize(Permission = 4)]
	public void CustomForm(Player commander, string title)
	{
		var image = new Image
		{
			Type = "path",
			Url = "textures/ui/teeth-glasse.png"
		};
		var url = new Image
		{
			Type = "url",
			Url = "https://media.tenor.com/e9vcnOU6RHwAAAAM/teeth-glasses.gif"
		};
		var customForm = new SimpleForm
		{
			Title = title,
			Content = "Grid form test",
			Buttons =
			[
				new Button
				{
					Text = "Button1",
					Image = image
				},
				new Button
				{
					Text = "Button2",
					Image = url
				},
				new Button { Text = "Button3" },
				new Button { Text = "Button4" },
				new Button { Text = "Button5" }
			]
		};

		commander.SendForm(customForm);
	}

	[Command(Name = "CustomParticle")]
	[Authorize(Permission = 4)]
	public void CustomParticle(Player commander, string identifier)
	{
		McpeSpawnParticleEffect pk = McpeSpawnParticleEffect.CreateObject();
		pk.particleName = identifier;
		pk.position = commander.KnownPosition.ToVector3();
		pk.dimensionId = 0;
		pk.entityId = -1;
		commander.Level.RelayBroadcast([commander], pk);
	}

	public enum CustomItems
	{
		HiveWings,
		CupLove,
		PigraidSpecial
	}
	
	[Command(Name = "CustomItem", Description = "Spawns the custom elytra in the chest slot")]
	[Authorize(Permission = 4)]
	public void CustomItem(Player commander, CustomItems name)
	{
		Item item;
		switch (name)
		{
			case CustomItems.CupLove:
				item = new ItemCupLove();
				break;
			case CustomItems.HiveWings:
				item = new ItemHiveEnderWings();
				break;
			case CustomItems.PigraidSpecial:
				item = new ItemPigraidSpecial();
				break;
			default:
				commander.SendMessage("Couldn't find the custom item");
				item = new ItemAir();
				break;
		}
		commander.Inventory.OffHandInventory.SetItem(item);
		commander.Inventory.SetFirstEmptySlot(item, true);
	}

	[Command(Name = "save-all", Description = "Saves the whole world")]
	[Authorize(Permission = 4)]
	public void SaveAll(Player commander)
	{
		commander.SendMessage("Saving the game (this may take a moment!)");
		commander.Level.WorldProvider.SaveChunks(true);
		commander.SendMessage("Saved the world");
	}

	[Command(Name = "about", Description = "About the server")]
	public string About()
	{
		return $"This server is running on PigNet-Pigraid {FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(MiNetServer)).Location).ProductVersion} for Minecraft Bedrock Edition {McpeProtocolInfo.GameVersion} ({McpeProtocolInfo.ProtocolVersion}). https://github.com/CobwebSMP/PigNet ";
	}

	[Command(Name = "op", Description = "Make player an operator")]
	[Authorize(Permission = 4)]
	public string MakeOperator(Player commander, Target target)
	{
		string body = target.Selector;

		if (target.Players != null)
		{
			var names = new List<string>();
			foreach (Player p in target.Players)
			{
				names.Add(p.Username);
				p.ActionPermissions = ActionPermissions.Operator;
				p.CommandPermission = 4;
				p.PermissionLevel = PermissionLevel.Operator;
				p.SendAbilities();
			}
			body = string.Join(", ", names);
		}
		else if (target.Entities != null)
		{
			var names = new List<string>();
			foreach (Entity p in target.Entities) names.Add(p.NameTag ?? p.EntityId + "");
			body = string.Join(", ", names);
		}

		return $"Oped: {body}";
	}

	[Command(Name = "", Description = "Place a block")]
	[Authorize(Permission = 4)]
	public string SetBlock(Player commander, BlockPos position, BlockTypeEnum tileName, int tileData = 0)
	{
		return $"Set block complete. {position.XRelative} {tileName.Value}";
	}

	[Command(Name = "give", Description = "Give item to Player")]
	[Authorize(Permission = 4)]
	public string Give(Player commander, Target player, ItemTypeEnum itemName, int amount = 1, int data = 0)
	{
		string body = player.Selector;

		if (player.Players != null)
		{
			var names = new List<string>();
			foreach (Player p in player.Players)
			{
				names.Add(p.Username);

				Item item = ItemFactory.GetItem(itemName.Value, (short) data, (byte) amount);

				if (item.Count > item.MaxStackSize) return $"The number you have entered ({amount}) is too big. It must be at most {item.MaxStackSize}";

				p.Inventory.SetFirstEmptySlot(item, true);
			}
			body = string.Join(", ", names);
		}


		return $"Gave {body} {amount} of {itemName.Value}.";
	}

	[Command(Name = "summon", Description = "Spawn entity")]
	[Authorize(Permission = 4)]
	public void Summon(Player player, EntityTypeEnum entityType, bool noAi = true, BlockPos spawnPos = null)
	{
		EntityType petType;
		try
		{
			petType = (EntityType) Enum.Parse(typeof(EntityType), entityType.Value, true);
		}
		catch (ArgumentException)
		{
			return;
		}

		if (!Enum.IsDefined(typeof(EntityType), petType))
		{
			player.SendMessage("No entity found");
			return;
		}

		PlayerLocation coordinates = player.KnownPosition;
		if (spawnPos != null)
		{
			if (spawnPos.XRelative)
				coordinates.X += spawnPos.X;
			else
				coordinates.X = spawnPos.X;

			if (spawnPos.YRelative)
				coordinates.Y += spawnPos.Y;
			else
				coordinates.Y = spawnPos.Y;

			if (spawnPos.ZRelative)
				coordinates.Z += spawnPos.Z;
			else
				coordinates.Z = spawnPos.Z;
		}

		Level world = player.Level;

		Mob mob = null;
		Entity entity = null;

		var type = (EntityType) (int) petType;
		switch (type)
		{
			case EntityType.Chicken:
				mob = new Chicken(world);
				break;
			case EntityType.Cow:
				mob = new Cow(world);
				break;
			case EntityType.Pig:
				mob = new Pig(world);
				break;
			case EntityType.Sheep:
				mob = new Sheep(world);
				break;
			case EntityType.Wolf:
				mob = new Wolf(world) { Owner = player };
				break;
			case EntityType.Villager:
				mob = new Villager(world);
				break;
			case EntityType.MushroomCow:
				mob = new MushroomCow(world);
				break;
			case EntityType.Squid:
				mob = new Squid(world);
				break;
			case EntityType.Rabbit:
				mob = new Rabbit(world);
				break;
			case EntityType.Bat:
				mob = new Bat(world);
				break;
			case EntityType.IronGolem:
				mob = new IronGolem(world);
				break;
			case EntityType.SnowGolem:
				mob = new SnowGolem(world);
				break;
			case EntityType.Ocelot:
				mob = new Ocelot(world);
				break;
			case EntityType.Zombie:
				mob = new Zombie(world);
				break;
			case EntityType.Creeper:
				mob = new Creeper(world);
				break;
			case EntityType.Skeleton:
				mob = new Skeleton(world);
				break;
			case EntityType.Spider:
				mob = new Spider(world);
				break;
			case EntityType.ZombiePigman:
				mob = new ZombiePigman(world);
				break;
			/*case EntityType.Slime:
				mob = new Slime(world);
				break;*/
			case EntityType.Enderman:
				mob = new Enderman(world);
				break;
			case EntityType.Silverfish:
				mob = new Silverfish(world);
				break;
			case EntityType.CaveSpider:
				mob = new CaveSpider(world);
				break;
			case EntityType.Ghast:
				mob = new Ghast(world);
				break;
			case EntityType.MagmaCube:
				mob = new MagmaCube(world);
				break;
			case EntityType.Blaze:
				mob = new Blaze(world);
				break;
			case EntityType.ZombieVillager:
				mob = new ZombieVillager(world);
				break;
			case EntityType.Witch:
				mob = new Witch(world);
				break;
			case EntityType.Stray:
				mob = new Stray(world);
				break;
			case EntityType.Husk:
				mob = new Husk(world);
				break;
			case EntityType.WitherSkeleton:
				mob = new WitherSkeleton(world);
				break;
			case EntityType.Guardian:
				mob = new Guardian(world);
				break;
			case EntityType.ElderGuardian:
				mob = new ElderGuardian(world);
				break;
			case EntityType.Horse:
				var random = new Random();
				mob = new Horse(world, random.NextDouble() < 0.10, random);
				break;
			case EntityType.PolarBear:
				mob = new PolarBear(world);
				break;
			case EntityType.Shulker:
				mob = new Shulker(world);
				break;
			case EntityType.Dragon:
				mob = new Dragon(world);
				break;
			case EntityType.SkeletonHorse:
				mob = new SkeletonHorse(world);
				break;
			case EntityType.Wither:
				mob = new Wither(world);
				break;
			case EntityType.Evoker:
				mob = new Evoker(world);
				break;
			case EntityType.Vindicator:
				mob = new Vindicator(world);
				break;
			case EntityType.Vex:
				mob = new Vex(world);
				break;
			case EntityType.Npc:
				if (Config.GetProperty("EnableEdu", false))
					mob = new Npc(world);
				else
					mob = new PlayerMob("test", world);
				break;
			case EntityType.Boat:
				entity = new Boat(world);
				break;
			case EntityType.ExperienceOrb:
				entity = new ExperienceOrb(world);
				break;
			case EntityType.Llama:
				entity = new Llama(world);
				break;
		}

		if (mob != null)
		{
			mob.NoAi = noAi;
			Vector3 direction = Vector3.Normalize(player.KnownPosition.GetHeadDirection()) * 1.5f;
			mob.KnownPosition = new PlayerLocation(coordinates.X + direction.X, coordinates.Y, coordinates.Z + direction.Z, coordinates.HeadYaw, coordinates.Yaw);
			mob.SpawnEntity();
		}
		else if (entity != null)
		{
			entity.NoAi = noAi;
			Vector3 direction = Vector3.Normalize(player.KnownPosition.GetHeadDirection()) * 1.5f;
			entity.KnownPosition = new PlayerLocation(coordinates.X + direction.X, coordinates.Y, coordinates.Z + direction.Z, coordinates.HeadYaw, coordinates.Yaw);
			entity.SpawnEntity();
		}
	}

	[Command(Name = "xp", Description = "Add XP to Player")]
	[Authorize(Permission = 4)]
	public string Xp(Player commander, int experience, Target player)
	{
		string body = player.Selector;

		if (player.Players != null)
		{
			var names = new List<string>();
			foreach (Player p in player.Players)
			{
				names.Add(p.Username);
				p.ExperienceManager.AddExperience(experience);
			}

			body = string.Join(", ", names);
		}

		return $"Gave {body} {experience} experience points.";
	}

	[Command(Name = "difficulty", Description = "Change worlds difficulty")]
	[Authorize(Permission = 4)]
	public string Difficulty(Player commander, Difficulty difficulty)
	{
		Level level = commander.Level;
		level.Difficulty = difficulty;
		foreach (Player player in level.GetAllPlayers()) player.SendSetDifficulty();

		return $"{commander.Username} set difficulty to {difficulty}";
	}

	[Command(Name = "time set", Description = "Changes or queries the world's game time")]
	[Authorize(Permission = 4)]
	public string TimeSet(Player commander, int time = 5000)
	{
		Level level = commander.Level;
		level.WorldTime = time;

		McpeSetTime message = McpeSetTime.CreateObject();
		message.time = (int) level.WorldTime;
		level.RelayBroadcast(message);

		return $"{commander.Username} sets time to {time}";
	}

	[Command(Name = "time set")]
	[Authorize(Permission = 4)]
	public string TimeSet(Player commander, DayNight time)
	{
		Level level = commander.Level;
		level.WorldTime = (int) time;

		McpeSetTime message = McpeSetTime.CreateObject();
		message.time = (int) level.WorldTime;
		level.RelayBroadcast(message);

		return $"{commander.Username} sets time to {time}";
	}

	[Command(Name = "tp", Aliases = new[] { "teleport" }, Description = "Teleports self to given position.")]
	[Authorize(Permission = 4)]
	public string Teleport(Player commander, BlockPos destination, int yrot = 90, int xrot = 0)
	{
		PlayerLocation coordinates = commander.KnownPosition;
		if (destination != null)
		{
			if (destination.XRelative)
				coordinates.X += destination.X;
			else
				coordinates.X = destination.X;

			if (destination.YRelative)
				coordinates.Y += destination.Y;
			else
				coordinates.Y = destination.Y;

			if (destination.ZRelative)
				coordinates.Z += destination.Z;
			else
				coordinates.Z = destination.Z;
		}

		ThreadPool.QueueUserWorkItem(delegate
		{
			commander.Teleport(new PlayerLocation
			{
				X = coordinates.X,
				Y = coordinates.Y,
				Z = coordinates.Z,
				Yaw = yrot,
				Pitch = xrot,
				HeadYaw = yrot
			});
		}, null);

		return $"{commander.Username} teleported to coordinates {coordinates.X},{coordinates.Y},{coordinates.Z}.";
	}

	[Command(Name = "tp", Aliases = new[] { "teleport" }, Description = "Teleports player to given coordinates.")]
	[Authorize(Permission = 4)]
	public string Teleport(Player commander, Target victim, BlockPos destination, int yrot = 90, int xrot = 0)
	{
		string body = victim.Selector;

		if (victim.Players != null)
		{
			var names = new List<string>();
			foreach (Player p in victim.Players)
			{
				names.Add(p.Username);

				ThreadPool.QueueUserWorkItem(delegate
				{
					PlayerLocation coordinates = p.KnownPosition;
					if (destination != null)
					{
						if (destination.XRelative)
							coordinates.X += destination.X;
						else
							coordinates.X = destination.X;

						if (destination.YRelative)
							coordinates.Y += destination.Y;
						else
							coordinates.Y = destination.Y;

						if (destination.ZRelative)
							coordinates.Z += destination.Z;
						else
							coordinates.Z = destination.Z;
					}

					p.Teleport(new PlayerLocation
					{
						X = coordinates.X,
						Y = coordinates.Y,
						Z = coordinates.Z,
						Yaw = yrot,
						Pitch = xrot,
						HeadYaw = yrot
					});
				}, null);
			}
			body = string.Join(", ", names);
		}

		return $"{body} teleported to new coordinates.";
	}


	[Command(Name = "tp", Aliases = new[] { "teleport" }, Description = "Teleports player to other player.")]
	[Authorize(Permission = 4)]
	public string Teleport(Player commander, Target victim, Target target)
	{
		string body = victim.Selector;

		if (target.Players == null || target.Players.Length != 1) return "Found not target for teleport";

		Player targetPlayer = target.Players.First();

		if (victim.Players != null)
		{
			var names = new List<string>();
			foreach (Player p in victim.Players)
			{
				names.Add(p.Username);

				ThreadPool.QueueUserWorkItem(delegate
				{
					PlayerLocation coordinates = targetPlayer.KnownPosition;
					p.Teleport(new PlayerLocation
					{
						X = coordinates.X,
						Y = coordinates.Y,
						Z = coordinates.Z,
						Yaw = coordinates.Yaw,
						Pitch = coordinates.Pitch,
						HeadYaw = coordinates.HeadYaw
					});
				}, null);
			}
			body = string.Join(", ", names);
		}


		return $"Teleported {body} to {targetPlayer.Username}.";
	}

	[Command(Name = "tp", Aliases = new[] { "teleport" }, Description = "Teleports self to other player.")]
	[Authorize(Permission = 4)]
	public string Teleport(Player commander, Target target)
	{
		if (target.Players == null || target.Players.Length != 1) return "Found not target for teleport";

		Player targetPlayer = target.Players.First();

		PlayerLocation coordinates = targetPlayer.KnownPosition;

		ThreadPool.QueueUserWorkItem(delegate
		{
			commander.Teleport(new PlayerLocation
			{
				X = coordinates.X,
				Y = coordinates.Y,
				Z = coordinates.Z,
				Yaw = coordinates.Yaw,
				Pitch = coordinates.Pitch,
				HeadYaw = coordinates.HeadYaw
			});
		}, null);


		return $"Teleported to {targetPlayer.Username}.";
	}

	[Command(Name = "enchant", Description = "Enchant item")]
	[Authorize(Permission = 4)]
	public void Enchant(Player commander, Target target, EnchantmentTypeEnum enchantmentTypeName, int level = 1)
	{
		try
		{
			Player targetPlayer = target.Players.First();
			Item item = targetPlayer.Inventory.GetItemInHand();
			if (item is ItemAir)
				return;

			EnchantingType enchanting;
			if (!Enum.TryParse(enchantmentTypeName.Value.Replace("_", ""), true, out enchanting))
				return;

			List<Enchanting> enchanings = item.GetEnchantings();
			enchanings.RemoveAll(ench => ench.Id == enchanting);
			enchanings.Add(new Enchanting
			{
				Id = enchanting,
				Level = (short) level
			});
			item.SetEnchantings(enchanings);
			targetPlayer.Inventory.SendSetSlot(targetPlayer.Inventory.InHandSlot);
		}
		catch (Exception e)
		{
			commander.SendMessage("Player wasn't found");
		}
	}

	[Command(Name = "gamemode", Description = "Change worlds GameMode")]
	[Authorize(Permission = 4)]
	public string GameMode(Player commander, GameMode gameMode, Target target = null)
	{
		Player targetPlayer = commander;
		if (target != null) targetPlayer = target.Players.First();

		switch (gameMode)
		{
			case Worlds.GameMode.Spectator:
				targetPlayer.SetSpectator(true);
				break;
			default:
				targetPlayer.SetGameMode(gameMode);
				break;
		}

		commander.Level.BroadcastMessage($"{targetPlayer.Username} changed to game mode {gameMode}.", TextPacketType.Raw);

		return $"Set {targetPlayer.Username} game mode to {gameMode}.";
	}

	[Command(Name = "gamerule", Description = "Change world Rules")]
	[Authorize(Permission = 4)]
	public string GameRule(Player player, GameRulesEnum rule)
	{
		return $"{rule.ToString().ToLower()}={player.Level.GetGameRule(rule).ToString().ToLower()}.";
	}

	[Command(Name = "gamerule", Description = "Change world Rules")]
	[Authorize(Permission = 4)]
	public string GameRule(Player player, GameRulesEnum rule, bool value)
	{
		player.Level.SetGameRule(rule, value);
		player.Level.BroadcastGameRules();
		return $"{player.Username} set {rule.ToString().ToLower()} to {value.ToString().ToLower()}.";
	}

	[Command(Name = "daylock", Description = "Always day")]
	[Authorize(Permission = 4)]
	public string Daylock(Player player, bool value)
	{
		Level level = player.Level;
		level.SetGameRule(GameRulesEnum.DoDaylightcycle, !value);
		level.BroadcastGameRules();

		level.WorldTime = 5000;

		McpeSetTime message = McpeSetTime.CreateObject();
		message.time = (int) level.WorldTime;
		level.RelayBroadcast(message);

		return $"{player.Username} set day to 5000 and locked time.";
	}

	[Command(Name = "fill", Description = "Fill specific with blocks")]
	[Authorize(Permission = 4)]
	public void Fill(Player commander, BlockPos from, BlockPos to, BlockTypeEnum tileName, int tileData = 0)
	{
	}

	[Command(Name = "kick", Description = "Remove player from the server")]
	[Authorize(Permission = 4)]
	public string Kick(Player commander, Target player, string reason = "")
	{
		if (player.Players != null)
			foreach (Player p in player.Players)
			{
				if (reason == "")
					p.Disconnect($"You have been kicked by {commander.Username}");
				else
					p.Disconnect($"You have been kicked by {commander.Username} for {reason}");
				return $"{p.Username} has been removed from the server.";
			}
		else
			return $"Couldn't kick {player}";
		return "";
	}

	[Command(Name = "tell", Description = "Send private message to player")]
	public string Tell(Player commander, Target player, string msg = "")
	{
		if (msg == "") return "Message can't be empty";
		if (player.Players != null)
			foreach (Player p in player.Players)
			{
				p.SendMessage(string.Format(ChatFormatting.Italic + ChatColors.Gray + "{0} whisper to you: {1}", commander.Username, msg), TextPacketType.Raw);
				return $"You whisper to {p.Username}: {msg}";
			}
		else
			return "Couldn't send message";
		return "";
	}

	[Command(Name = "weather", Description = "Sets the weather")]
	[Authorize(Permission = 4)]
	public string Weather(Player commander, WeatherManager.weatherTypes weather)
	{
		var change = new WeatherManager(commander.Level);
		switch (weather)
		{
			case WeatherManager.weatherTypes.clear:
				change.setWeather(WeatherManager.weatherTypes.clear);
				return "Changing to clear weather";
			case WeatherManager.weatherTypes.rain:
				change.setWeather(WeatherManager.weatherTypes.rain);
				return "Changing to rainy weather";
			case WeatherManager.weatherTypes.thunder:
				change.setWeather(WeatherManager.weatherTypes.thunder);
				return "Changing to rain and thunder";
			default:
				return "";
		}
	}

	[Command(Name = "fog", Description = "Change level fog settings")]
	[Authorize(Permission = 4)]
	public void fog(Player commander, fogMode action, string fogID)
	{
		if (action == fogMode.push)
		{
			McpePlayerFog msg = McpePlayerFog.CreateObject();
			msg.fogstack = new fogStack(fogID);
			commander.Level.RelayBroadcast(msg);
			commander.Level.fog = fogID;
			commander.SendMessage("Fog setting was added successfully");
		}
		else if (action == fogMode.remove)
		{
			McpePlayerFog msg = McpePlayerFog.CreateObject();
			msg.fogstack = new fogStack("minecraft:fog_default");
			commander.Level.RelayBroadcast(msg);
			commander.Level.fog = "";
			commander.SendMessage("Fog setting was removed successfully");
		}
	}
}