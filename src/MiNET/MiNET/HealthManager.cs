﻿#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/MiNET/blob/master/LICENSE. 
// The License is based on the Mozilla Public License Version 1.1, but Sections 14 
// and 15 have been added to cover use of software over a computer network and 
// provide for limited attribution for the Original Developer. In addition, Exhibit A has 
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is MiNET.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2018 Niclas Olofsson. 
// All Rights Reserved.

#endregion

using System;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using MiNET.Effects;
using MiNET.Entities;
using MiNET.Items;
using MiNET.Net;
using MiNET.Sounds;
using MiNET.Utils;
using MiNET.Utils.Vectors;
using MiNET.Worlds;

namespace MiNET
{
	public enum DamageCause
	{
		// Shouldn't be used, but it's when the attack source is unknown
		[Description("{0} went MIA")] Unknown,
		// Source: https://minecraft.fandom.com/wiki/Death_messages#Bedrock_Edition
		// Appears when the player is killed by being 64 blocks below the lowest point where blocks can be placed
		[Description("{0} fell out of the world")] Void,
		// Appears when the player is killed by an arrow shot from a dispenser or summoned with /summon
		[Description("{0} was slain by Arrow")] SlainByArrow,
		// Appears when the player is killed by an arrow shot by a player or mob
		[Description("{0} was shot by {1}")] ShotByArrow,
		// Appears when the player is killed because they were touching a cactus
		[Description("{0} was pricked to death")] KilledByCatctus,
		// Appears when the player runs out of air underwater and is killed from drowning damage
		[Description("{0} drowned")] Drowning,
		// Appears when the player is killed by hitting a wall while flying with elytra on
		[Description("{0} experienced kinectic energy")] ElytraCollision,
		// Appears when the player is killed by an end crytal, a bed exploding in the Nether or the End, or by a charged respawn anchor exploding in the Overworld or the End
		[Description("{0} blew up")] BlockExplosion,
		// Appears when the player is killed by TNT activated by redstone mechanisms, fire, or dispensed out from a dispenser
		[Description("{0} was blown up by Block of TNT")] TNTExplosion,
		// Appears when the player is killed by an entity that exploded, or by TNT activated by a player or mob
		[Description("{0} was blown up by {1}")] EntityExplosion,
		// Appears when the player is killed because they were in a sweet berry bush.
		[Description("{0} was poked to death by a sweet berry bush")] SweetBerryBush,
		// Appears when the player is killed by a less than 5 block fall, ender pearl damage, or falling while riding an entity that died due to fall damage
		[Description("{0} hit the ground too hard")] Fall,
		// Appears when the player is killed by a greater than 5 block fall
		[Description("{0} fell from a high place")] FallFromHighPlace,
		// Appears when the player is killed by an anvil falling on their head
		[Description("{0} was squashed by a falling anvil")] FallingAnvil,
		// Appears when the player is killed by a falling block (other than an anvil) modified to inflict damage
		[Description("{0} was squashed by a falling block")] FallingBlock,
		// Appears when the player is killed because they were in a fire source block
		[Description("{0} went up in flames")] Fire,
		// Appears when the player is killed because they were on fire, but not in a fire source block
		[Description("{0} burned to death")] FireTick,
		// Appears when the player is killed by the explosion of a firework rocket
		[Description("{0} went off with a bang")] FireworkExplosion,
		// Appears when the player is killed because they were in lava
		[Description("{0} tried to swim in lava")] Lava,
		// Appears when the player is killed by a lightning bolt
		[Description("{0} was struck by lightning")] Lightning,
		// Appears when the player is killed because they were standing on a magma block
		[Description("{0} discovered floor was lava")] MagmaBlock,
		// Appears when the player is killed by a potion of Harming shot from a dispenser, by Instant Damage given with /effect or by an evoker fang summoned with /summon
		[Description("{0} was killed by magic")] Magic,
		// Appears when the player is killed by a potion or arrow of Harming shot by a player or mob, or by an evoker fang summoned by an evoker
		[Description("{0} was killed by {1} using magic")] MobMagic,
		// Appears when the player is hurt by a player or mob and killed
		[Description("{0} was slain by {1}")] EntityAttack,
		// Appears when the player is hurt by a player holding a renamed item and killed
		[Description("{0} was slain by {1} using {2}")] CustomItem,
		// Appears when the player is killed by a fireball shot from a dispenser
		[Description("{0} was slain by Small Fireball")] SmallFireball,
		// Appears when the player plays in hard difficulty and is killed by hunger damage because their hunger bar was at 0
		[Description("{0} starved to death")] Starving,
		// Appears when the player is killed because they were inside of a non-transparent block.
		[Description("{0} suffocated in a wall")] SuffocatedInWall,
		// Appears when the player is killed because they hurt a guardian, elder guardian, or a player or mob wearing armor enchanted with Thorns.
		[Description("{0} was killed trying to hurt {1}")] KilledByThorns,
		// Appears when the player is killed by a trident shot by a player or mob, or from a dispenser or summoned with /summon.
		[Description("{0} was impaled to death by {1}")] ImpaledByTrident,
		// Appears when the player is killed by being more than 64 blocks below the bottom of the world.
		[Description("{0} fell out of the world")] FellOutOfWorld,
		// Appears when the player is killed by the wither status effect.
		[Description("{0} withered away")] WitherEffect,
		// Appears when the player is killed by /kill.
		[Description("{0} died")] KilledByCommand,
		// Appears when the player is killed by a fireball shot by a player or mob.
		[Description("{0} was fireballed by {1}")] KilledByFireball,
		// Appears when the player is killed by a shulker bullet shot by a shulker.
		[Description("{0} was sniped by {1}")] KilledByShulkerBullet,
		// Appears when the player is killed by a llama spit shot by a llama.
		[Description("{0} was spitballed by {1}")] KilledByLlamaSpit,
		// Appears when the player is killed because they were in powder snow for too long.
		[Description("{0} froze to death")] Freezing,
		// Appears when the player is killed by falling stalactite.
		[Description("{0} was skewered by a falling stalactite")] KilledByFallingStalactite,
		// Appears when the player falls on a stalagmite and dies.
		[Description("{0} was impaled on a stalagmite")] KilledByStalagmite,
		// Appears when the player is killed by a warden using its sonic boom.
		[Description("{0} was obliterated by a sonically-charged shriek whilst trying to escape {1}")] KilledByWardenSonicBoom,
	}

	public class HealthManager
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(HealthManager));

		public Entity Entity { get; set; }
		public int MaxHealth { get; set; } = 200;
		public int Health { get; set; }
		public float Absorption { get; set; }
		public short MaxAir { get; set; } = 400;
		public short Air { get; set; }
		public bool IsDead { get; set; }
		public int FireTick { get; set; }
		public int SuffocationTicks { get; set; }
		public int LavaTicks { get; set; }
		public int CooldownTick { get; set; }
		public bool CheckCooldown { get; set; } = true;
		public bool IsOnFire { get; set; }
		public bool IsInvulnerable { get; set; }
		public DamageCause LastDamageCause { get; set; }
		public Entity LastDamageSource { get; set; }

		public HealthManager(Entity entity)
		{
			Entity = entity;
			ResetHealth();
		}

		public int Hearts
		{
			get { return (int) Math.Ceiling(Health / 10d); }
		}

		public int MaxHearts
		{
			get { return (int) Math.Ceiling(MaxHealth / 10d); }
		}

		public virtual void Regen(int amount = 1)
		{
			if (IsDead)
			{
				Kill();
				return;
			}
			Health += amount * 10;
			if (Health > MaxHealth) Health = MaxHealth;

			var player = Entity as Player;
			if (player != null)
			{
				player.SendUpdateAttributes();
			}
		}

		public virtual void TakeHit(Entity source, int damage = 1, DamageCause cause = DamageCause.Unknown)
		{
			TakeHit(source, null, damage, cause);
		}

		public virtual void TakeHit(Entity source, Item tool, int damage = 1, DamageCause cause = DamageCause.Unknown)
		{
			var player = Entity as Player;
			if (player != null && player.GameMode != GameMode.Survival) return;
			if (player != null && player.IsInvicible) return;

			if (CooldownTick > 0 && CheckCooldown) return;

			LastDamageSource = source;
			LastDamageCause = cause;
			if (Absorption > 0)
			{
				float abs = Absorption * 10;
				abs = abs - damage;
				if (abs < 0)
				{
					Absorption = 0;
					damage = Math.Abs((int) Math.Floor(abs));
				}
				else
				{
					Absorption = abs / 10f;
					damage = 0;
				}
			}

			if (cause == DamageCause.Starving)
			{
				if (Entity.Level.Difficulty <= Difficulty.Easy && Hearts <= 10) return;
				if (Entity.Level.Difficulty <= Difficulty.Normal && Hearts <= 1) return;
			}

			Health -= damage * 10;
			if (Health < 0)
			{
				OnPlayerTakeHit(new HealthEventArgs(this, source, Entity));
				Health = 0;
				Kill();
				return;
			}

			if (player != null)
			{
				player.Inventory.ArmorInventory.DamageAll();
				player.HungerManager.IncreaseExhaustion(0.3f);
				player.SendUpdateAttributes();
			}

			Entity.BroadcastEntityEvent();

			if (source != null)
			{
				DoKnockback(source, tool);
			}

			CooldownTick = 10;

			OnPlayerTakeHit(new HealthEventArgs(this, source, Entity));
		}

		protected virtual void DoKnockback(Entity source, Item tool)
		{
			double dx = source.KnownPosition.X - Entity.KnownPosition.X;

			Random rand = new Random();
			double dz;
			for (dz = source.KnownPosition.Z - Entity.KnownPosition.Z; dx * dx + dz * dz < 0.00010; dz = (rand.NextDouble() - rand.NextDouble()) * 0.01D)
			{
				dx = (rand.NextDouble() - rand.NextDouble()) * 0.01D;
			}

			double knockbackForce = Math.Sqrt(dx * dx + dz * dz);
			float knockbackMultiplier = 0.4F;

			double motX = 0;
			motX -= dx / knockbackForce * knockbackMultiplier;
			double motY = knockbackMultiplier * 0.89; // reduced by 11% (vertical)
			double motZ = 0;
			motZ -= (dz / knockbackForce * knockbackMultiplier) * 0.95; // reduced by 5% (horizontal)
			if (motY > 0.4)
			{
				motY = 0.4;
			}

			var velocity = new Vector3((float) motX, (float) motY + 0.0f, (float) motZ);

			if (tool != null)
			{
				var knockback = tool.GetEnchantingLevel(EnchantingType.Knockback);
				velocity += Vector3.Normalize(velocity) * new Vector3(knockback * 0.5f, 0.1f, knockback * 0.5f);
			}

			Entity.Knockback(velocity);
		}

		protected virtual void OnPlayerKilled(HealthEventArgs e)
		{
			EventHandler<HealthEventArgs> handler = PlayerKilled;
			if (handler != null) handler(this, e);
		}

		public event EventHandler<HealthEventArgs> PlayerTakeHit;

		protected virtual void OnPlayerTakeHit(HealthEventArgs e)
		{
			EventHandler<HealthEventArgs> handler = PlayerTakeHit;
			if (handler != null) handler(this, e);
		}

		public virtual void Ignite(int ticks = 300)
		{
			if (IsDead) return;

			Player player = Entity as Player;
			if (player != null)
			{
				ticks -= ticks * DamageCalculator.CalculateFireTickReduction(player);
			}

			ticks = Math.Max(0, ticks);

			FireTick = ticks;
			if (!IsOnFire)
			{
				IsOnFire = true;
				Entity.BroadcastSetEntityData();
			}
		}

		public event EventHandler<HealthEventArgs> PlayerKilled;

		private object _killSync = new object();

		public virtual void Kill()
		{
			var player = Entity as Player;
			lock (_killSync)
			{
				if (IsDead) return;
				if (player != null)
				{
					if (player.Inventory.GetItemInHand() is ItemTotemOfUndying || player.Inventory.OffHandInventory.GetItem() is ItemTotemOfUndying)
					{
						Health = 2;

						player.RemoveAllEffects();
						player.SetEffect(new Regeneration() { Duration = 900, Level = 1 });
						player.SetEffect(new FireResistance() { Duration = 800 });
						player.SetEffect(new Absorption() { Duration = 100, Level = 1 });

						var sound = new Sound((short) LevelEventType.SoundTotemUsed, player.KnownPosition);
						sound.Spawn(player.Level);

						var entityEvent = McpeEntityEvent.CreateObject();
						entityEvent.runtimeEntityId = 2;
						entityEvent.eventId = 65; // 65 - consume totem. todo make entity event enum table
						player.SendPacket(entityEvent);

						if (player.Inventory.GetItemInHand() is ItemTotemOfUndying)
						{
							player.Inventory.SetInventorySlot(player.Inventory.InHandSlot, new ItemAir());
						}
						else
						{
							player.Inventory.OffHandInventory.SetItem(new ItemAir());
							player.SendPlayerInventory();
						}
						return;
					}
				}
				IsDead = true;

				Health = 0;
			}
			

			if (player != null)
			{
				player.SendUpdateAttributes();
			}


			Entity.BroadcastEntityEvent();
			OnPlayerKilled(new HealthEventArgs(this, LastDamageSource, Entity));

			if (player != null)
			{
				//SendWithDelay(2000, () =>
				//{
				//});

				Entity.BroadcastSetEntityData();
				Entity.DespawnEntity();

				if (!Entity.Level.KeepInventory)
				{
					player.DropInventory();
				}

				var mcpeRespawn = McpeRespawn.CreateObject();
				mcpeRespawn.x = player.SpawnPosition.X;
				mcpeRespawn.y = player.SpawnPosition.Y;
				mcpeRespawn.z = player.SpawnPosition.Z;
				mcpeRespawn.state = (byte) McpeRespawn.RespawnState.Search;
				mcpeRespawn.runtimeEntityId = player.EntityId;
				player.SendPacket(mcpeRespawn);
			}
			else
			{
				if (LastDamageSource is Player && Entity.Level.DoMobloot)
				{
					var drops = Entity.GetDrops();
					foreach (var drop in drops)
					{
						Entity.Level.DropItem(Entity.KnownPosition.ToVector3(), drop);
					}
				}

				// This is semi-good, but we need to give the death-animation time to play.
				
				_ = SendWithDelay(2000, () =>
				{
					Entity.BroadcastSetEntityData();
					Entity.DespawnEntity();
				});
			}
		}

		private async Task SendWithDelay(int delay, Action action)
		{
			await Task.Delay(delay);
			action();
		}

		public virtual void ResetHealth()
		{
			Health = MaxHealth;
			Air = MaxAir;
			IsOnFire = false;
			FireTick = 0;
			SuffocationTicks = 10;
			LavaTicks = 0;
			IsDead = false;
			CooldownTick = 0;
			LastDamageCause = DamageCause.Unknown;
			LastDamageSource = null;
		}

		public virtual void OnTick()
		{
			if (!Entity.IsSpawned) return;

			if (IsDead) return;

			if (CooldownTick > 0)
			{
				CooldownTick--;
			}
			else
			{
				LastDamageSource = null;
			}

			if (IsInvulnerable) Health = MaxHealth;

			if (Health <= 0)
			{
				Kill();
				return;
			}

			if (Entity.KnownPosition.Y < 0 && !IsDead)
			{
				CooldownTick = 0;
				TakeHit(null, 300, DamageCause.Void);
				return;
			}

			if (IsInWater(Entity.KnownPosition))
			{
				Entity.IsInWater = true;

				Air--;
				if (Air <= 0)
				{
					if (Math.Abs(Air) % 10 == 0)
					{
						TakeHit(null, 1, DamageCause.Drowning);
						Entity.BroadcastSetEntityData();
					}
				}

				Entity.BroadcastSetEntityData();
			}
			else
			{
				Air = MaxAir;

				if (Entity.IsInWater)
				{
					Entity.IsInWater = false;
					Entity.BroadcastSetEntityData();
				}
			}

			if (IsOnFire && (Entity.IsInWater || IsStandingInWater(Entity.KnownPosition)))
			{
				IsOnFire = false;
				FireTick = 0;
				Entity.BroadcastSetEntityData();
			}

			if (IsInOpaque(Entity.KnownPosition))
			{
				if (SuffocationTicks <= 0)
				{
					TakeHit(null, 1, DamageCause.SuffocatedInWall);
					Entity.BroadcastSetEntityData();

					SuffocationTicks = 10;
				}
				else
				{
					SuffocationTicks--;
				}
			}
			else
			{
				SuffocationTicks = 10;
			}

			if (IsInLava(Entity.KnownPosition))
			{
				if (LastDamageCause.Equals(DamageCause.Lava))
				{
					FireTick += 2;
				}
				else
				{
					Ignite(300);
				}

				if (LavaTicks <= 0)
				{
					TakeHit(null, 4, DamageCause.Lava);
					Entity.BroadcastSetEntityData();

					LavaTicks = 10;
				}
				else
				{
					LavaTicks--;
				}
			}
			else
			{
				LavaTicks = 0;
			}

			if (!IsInLava(Entity.KnownPosition) && IsOnFire)
			{
				if (FireTick <= 0)
				{
					IsOnFire = false;
					Entity.BroadcastSetEntityData();
				}
				else if (FireTick % 20 == 0)
				{
					var player = Entity as Player;
					if (player != null)
					{
						player.DamageCalculator.CalculatePlayerDamage(null, player, null, 1, DamageCause.FireTick);
						TakeHit(null, 1, DamageCause.FireTick);
					}
					else
					{
						TakeHit(null, 1, DamageCause.FireTick);
					}
					//Entity.BroadcastSetEntityData();
				}

				FireTick--;
			}
		}

		public bool IsInWater(PlayerLocation playerPosition)
		{
			if (playerPosition.Y < 0 || playerPosition.Y > 255) return false;

			float y = playerPosition.Y + 1.62f;

			BlockCoordinates waterPos = new BlockCoordinates
			{
				X = (int) Math.Floor(playerPosition.X),
				Y = (int) Math.Floor(y),
				Z = (int) Math.Floor(playerPosition.Z)
			};

			var block = Entity.Level.GetBlock(waterPos);

			if (block == null || (block.Id != 8 && block.Id != 9)) return false;

			return y < Math.Floor(y) + 1 - ((1f / 9f) - 0.1111111);
		}

		public bool IsStandingInWater(PlayerLocation playerPosition)
		{
			if (playerPosition.Y < 0 || playerPosition.Y > 255) return false;

			var block = Entity.Level.GetBlock(playerPosition);

			if (block == null || (block.Id != 8 && block.Id != 9)) return false;

			return playerPosition.Y < Math.Floor(playerPosition.Y) + 1 - ((1f / 9f) - 0.1111111);
		}

		private bool IsInLava(PlayerLocation playerPosition)
		{
			if (playerPosition.Y < 0 || playerPosition.Y > 255) return false;

			var block = Entity.Level.GetBlock(playerPosition);

			if (block == null || (block.Id != 10 && block.Id != 11)) return false;

			return playerPosition.Y < Math.Floor(playerPosition.Y) + 1 - ((1f / 9f) - 0.1111111);
		}

		private bool IsInOpaque(PlayerLocation playerPosition)
		{
			if (playerPosition.Y < 0 || playerPosition.Y > 255) return false;

			BlockCoordinates solidPos = (BlockCoordinates) playerPosition;
			if (Entity.Height >= 1)
			{
				solidPos.Y += 1;
			}

			var block = Entity.Level.GetBlock(solidPos);

			if (block == null) return false;

			return !block.IsTransparent;
		}

		public static string GetDescription(Enum value)
		{
			FieldInfo fi = value.GetType().GetField(value.ToString());
			DescriptionAttribute[] attributes = (DescriptionAttribute[]) fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

			if (attributes.Length > 0)
				return attributes[0].Description;

			return value.ToString();
		}
	}

	public class HealthEventArgs : EventArgs
	{
		public Entity SourceEntity { get; set; }
		public Entity TargetEntity { get; set; }
		public HealthManager HealthManager { get; set; }

		public HealthEventArgs(HealthManager healthManager, Entity sourceEntity, Entity targetEntity)
		{
			SourceEntity = sourceEntity;
			TargetEntity = targetEntity;
			HealthManager = healthManager;
		}
	}
}