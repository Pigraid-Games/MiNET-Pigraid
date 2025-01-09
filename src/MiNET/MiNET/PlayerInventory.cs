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
using System.Collections.Generic;
using System.Linq;
using fNbt;
using log4net;
using MiNET.Blocks;
using MiNET.Entities;
using MiNET.Entities.World;
using MiNET.Inventories;
using MiNET.Items;
using MiNET.Net;
using MiNET.Utils;
using MiNET.Utils.Vectors;
using MiNET.Worlds;

namespace MiNET
{
	public class PlayerInventory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(PlayerInventory));

		public const int HotbarSize = 9;
		public const int InventorySize = HotbarSize + 36;
		public Player Player { get; }

		public List<Item> Slots { get; }

		public int InHandSlot { get; set; }
		public Item OffHand { get; set; } = new ItemAir();

		public CursorInventory UiInventory { get; set; } = new CursorInventory();

		public ArmorInventory ArmorInventory { get; set; }


		public PlayerInventory(Player player)
		{
			Player = player;
			Slots = Enumerable.Repeat((Item) new ItemAir(), InventorySize).ToList();
			InHandSlot = 0;
			ArmorInventory = new ArmorInventory(player);
		}

		public virtual Item GetItemInHand()
		{
			return Slots[InHandSlot] ?? new ItemAir();
		}

		public virtual void DamageItemInHand(ItemDamageReason reason, Entity target, Block block)
		{
			if (Player.GameMode != GameMode.Survival) return;

			var itemInHand = GetItemInHand();

			var unbreakingLevel = itemInHand.GetEnchantingLevel(EnchantingType.Unbreaking);
			if (unbreakingLevel > 0)
			{
				if (new Random().Next(1 + unbreakingLevel) != 0) return;
			}


			if (itemInHand.DamageItem(Player, reason, target, block))
			{
				Slots[InHandSlot] = new ItemAir();

				var sound = McpeLevelSoundEventOld.CreateObject();
				sound.soundId = 5;
				sound.blockId = -1;
				sound.entityType = 1;
				sound.position = Player.KnownPosition;
				Player.Level.RelayBroadcast(sound);
			}

			if (itemInHand.ExtraData != null)
			{
				if (itemInHand.ExtraData.Get<NbtInt>("Damage") != null)
				{
					itemInHand.ExtraData.Get<NbtInt>("Damage").Value = itemInHand.Damage;
				}
			}

			SendSetSlot(InHandSlot);
		}

		[Wired]
		public virtual void SetInventorySlot(int slot, Item item, bool forceReplace = false)
		{
			if (item == null || item.Count <= 0) item = new ItemAir();

			UpdateInventorySlot(slot, item, forceReplace);
			Player.SendPlayerInventory();
		}

		public virtual void UpdateInventorySlot(int slot, Item item, bool forceReplace = false)
		{
			var existing = Slots[slot];
			if (forceReplace || existing.Id != item.Id)
			{
				Slots[slot] = item;
				existing = item;
			}

			existing.UniqueId = item.UniqueId;
			existing.Count = item.Count;
			existing.Metadata = item.Metadata;
			existing.ExtraData = item.ExtraData;
		}

		public ItemStacks GetSlots()
		{
			ItemStacks slotData = new ItemStacks();
			for (int i = 0; i < Slots.Count; i++)
			{
				if (Slots[i].Count == 0) Slots[i] = new ItemAir();
				slotData.Add(Slots[i]);
			}

			return slotData;
		}

		public ItemStacks GetUiSlots()
		{
			ItemStacks slotData = new ItemStacks();
			for (int i = 0; i < UiInventory.Slots.Count; i++)
			{
				if (UiInventory.Slots[i].Count == 0) UiInventory.Slots[i] = new ItemAir();
				slotData.Add(UiInventory.Slots[i]);
			}

			return slotData;
		}

		public ItemStacks GetOffHand()
		{
			return new ItemStacks
			{
				OffHand ?? new ItemAir(),
			};
		}

		public virtual bool SetFirstEmptySlot(Item item, bool update)
		{
			for (int si = 0; si < Slots.Count; si++)
			{
				Item existingItem = Slots[si];

				// This needs to also take extradata into account when comparing.
				if (existingItem.Equals(item) && existingItem.Count < existingItem.MaxStackSize)
				{
					int take = Math.Min(item.Count, existingItem.MaxStackSize - existingItem.Count);
					existingItem.Count += (byte) take;
					item.Count -= (byte) take;
					if (update)
						SendSetSlot(si);

					if (item.Count <= 0)
					{
						return true;
					}
				}
			}

			for (int si = 0; si < Slots.Count; si++)
			{
				if (FirstEmptySlot(item, update, si))
					return true;
			}

			return false;
		}

		private bool FirstEmptySlot(Item item, bool update, int si)
		{
			Item existingItem = Slots[si];

			if (existingItem is ItemAir || existingItem.Id == 0 || existingItem.Id == -1)
			{
				Slots[si] = (Item) item.Clone();
				item.Count = 0;
				if (update) SendSetSlot(si);
				return true;
			}

			return false;
		}

		public bool AddItem(Item item, bool update)
		{
			return SetFirstEmptySlot(item, update);
		}


		public virtual void SetHeldItemSlot(int selectedHotbarSlot, bool sendToPlayer = true)
		{
			InHandSlot = selectedHotbarSlot;

			if (GetItemInHand() is ItemMap)
			{
				long mapUuid = GetItemInHand().ExtraData.Get<NbtLong>("map_uuid").Value;
				if (Player.Level.TryGetEntity(mapUuid, out MapEntity mapEntity))
				{
					var mapInfo = mapEntity.MapInfo;
					mapInfo.UpdateType = 8;

					var msg = McpeClientboundMapItemData.CreateObject();
					msg.mapinfo = mapInfo;

					Player.SendPacket(msg);
				}
			}

			if (sendToPlayer)
			{
				var order = McpeMobEquipment.CreateObject();
				order.runtimeEntityId = EntityManager.EntityIdSelf;
				order.item = GetItemInHand();
				order.selectedSlot = (byte) InHandSlot;
				order.slot = (byte) InHandSlot;
				Player.SendPacket(order);
			}

			var broadcast = McpeMobEquipment.CreateObject();
			broadcast.runtimeEntityId = Player.EntityId;
			broadcast.item = GetItemInHand();
			broadcast.selectedSlot = (byte) InHandSlot;
			broadcast.slot = (byte) InHandSlot;
			Player.Level?.RelayBroadcast(Player, broadcast);
		}

		/// <summary>
		///     Empty the specified slot
		/// </summary>
		/// <param name="slot">The slot to empty.</param>
		public void ClearInventorySlot(byte slot)
		{
			SetInventorySlot(slot, new ItemAir());
		}

		public bool HasItem(Item item)
		{
			for (byte i = 0; i < Slots.Count; i++)
			{
				if (Slots[i].Id == item.Id && Slots[i].Metadata == item.Metadata)
				{
					return true;
				}
			}

			return false;
		}

		public void RemoveItems(short id, byte count)
		{
			if (count <= 0) return;

			for (byte i = 0; i < Slots.Count; i++)
			{
				if (count <= 0) break;

				var slot = Slots[i];
				if (slot.Id == id)
				{
					if (Slots[i].Count >= count)
					{
						Slots[i].Count -= count;
						count = 0;
					}
					else
					{
						count -= Slots[i].Count;
						Slots[i].Count = 0;
					}

					if (slot.Count == 0)
					{
						Slots[i] = new ItemAir();
					}

					SendSetSlot(i);
				}
			}
		}

		public virtual void SendSetSlot(int slot, byte ContainerId = 12)
		{
			//Log.Warn(ContainerId);
			uint inventoryId;
			uint invId = 255;
			Inventory inv = null;

			if (Player._openInventory is Inventory inventory)
			{
				invId = inventory.WindowsId;
				inv = inventory;
			}

			switch (ContainerId)
			{
				case 0:
				case 1:
				case 13:
				case 22:
					inventoryId = 124; //UI
					break;
				case 6:
				case 12:
				case 28:
				case 29:
					inventoryId = 0; //Player inventory
					break;
				case 7:
				case 24:
				case 25:
				case 30:
				case 45:
					inventoryId = invId; //Container
					break;
				default:
					inventoryId = 0;
					Log.Warn($"SendSetSlot: Unknown ContainerId: {ContainerId}");
					break;
			}

			Item item;

			if (inventoryId == 124)
			{
				item = UiInventory.Slots[slot];
			}
			else if (inventoryId == invId)
			{
				item = Player.Level.InventoryManager.GetInventory(inv.Id).Slots[slot];
			}
			else
			{
				item = Slots[slot];
			}

			var sendSlot = McpeInventorySlot.CreateObject();
			sendSlot.inventoryId = inventoryId;
			sendSlot.slot = (uint) slot;
			sendSlot.item = item;
			Player.SendPacket(sendSlot);
		}

		public void Clear()
		{
			for (int i = 0; i < Slots.Count; ++i)
			{
				if (Slots[i] == null || Slots[i].Id != 0) Slots[i] = new ItemAir();
			}
			
			UiInventory.Clear();

			if (OffHand.Id != 0) OffHand = new ItemAir();
			Player.SendPlayerInventory();
		}
	}
}
