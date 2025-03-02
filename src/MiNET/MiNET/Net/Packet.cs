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
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using fNbt;
using log4net;
using Microsoft.IO;
using MiNET.Blocks;
using MiNET.Crafting;
using MiNET.Items;
using MiNET.Net.Items;
using MiNET.Net.RakNet;
using MiNET.Utils;
using MiNET.Utils.IO;
using MiNET.Utils.Metadata;
using MiNET.Utils.Nbt;
using MiNET.Utils.Skins;
using MiNET.Utils.Vectors;
using Newtonsoft.Json;
using static MiNET.Net.McpePlayerAuthInput;

namespace MiNET.Net;

public abstract class Packet
{
	private const int ShieldId = 355;

	private const byte Shapeless = 0;
	private const byte Shaped = 1;
	private const byte Furnace = 2;
	private const byte FurnaceData = 3;
	private const byte Multi = 4;
	private const byte ShulkerBox = 5;
	private const byte ShapelessChemistry = 6;
	private const byte ShapedChemistry = 7;
	private const byte SmithingTransform = 8;
	private const byte SmithingTrim = 9;


	private const int MapUpdateFlagTexture = 0x02;
	private const int MapUpdateFlagDecoration = 0x04;
	private const int MapUpdateFlagInitialisation = 0x08;
	private static readonly ILog Log = LogManager.GetLogger(typeof(Packet));

	private static readonly RecyclableMemoryStreamManager _streamManager = new();
	private static readonly ConcurrentDictionary<int, bool> _isLob = new();

	private readonly object _encodeSync = new();
	private protected Stream _buffer;

	private byte[] _encodedMessage;

	protected MemoryStreamReader _reader; // new construct for reading
	private BinaryWriter _writer;

	[JsonIgnore] public bool ForceClear;

	[JsonIgnore] public int Id;
	[JsonIgnore] public bool IsMcpe;

	[JsonIgnore] public ReliabilityHeader ReliabilityHeader = new();

	public Packet()
	{
		Timer.Start();
	}

	[JsonIgnore] public bool NoBatch { get; set; }

	[JsonIgnore] public ReadOnlyMemory<byte> Bytes { get; private set; }
	[JsonIgnore] public Stopwatch Timer { get; } = Stopwatch.StartNew();

	public void Write(sbyte value)
	{
		_writer.Write(value);
	}

	public sbyte ReadSByte()
	{
		return (sbyte) _reader.ReadByte();
	}

	public void Write(byte value)
	{
		_writer.Write(value);
	}

	public byte ReadByte()
	{
		return (byte) _reader.ReadByte();
	}

	public void Write(bool value)
	{
		Write((byte) (value ? 1 : 0));
	}

	public bool ReadBool()
	{
		return _reader.ReadByte() != 0;
	}

	public void Write(Memory<byte> value)
	{
		Write((ReadOnlyMemory<byte>) value);
	}

	public void Write(ReadOnlyMemory<byte> value)
	{
		if (value.IsEmpty)
		{
			Log.Warn("Trying to write empty Memory<byte>");
			return;
		}
		_writer.Write(value.Span);
	}

	public void Write(byte[] value)
	{
		if (value == null)
		{
			Log.Warn("Trying to write null byte[]");
			return;
		}

		_writer.Write(value);
	}

	public ReadOnlyMemory<byte> Slice(int count)
	{
		return _reader.Read(count);
	}

	public ReadOnlyMemory<byte> ReadReadOnlyMemory(int count, bool slurp = false)
	{
		if (!slurp && count == 0) return Memory<byte>.Empty;

		if (count == 0) count = (int) (_reader.Length - _reader.Position);

		ReadOnlyMemory<byte> readBytes = _reader.Read(count);
		if (readBytes.Length != count) throw new ArgumentOutOfRangeException($"Expected {count} bytes, only read {readBytes.Length}.");
		return readBytes;
	}

	public byte[] ReadBytes(int count, bool slurp = false)
	{
		if (!slurp && count == 0) return new byte[0];

		if (count == 0) count = (int) (_reader.Length - _reader.Position);

		ReadOnlyMemory<byte> readBytes = _reader.Read(count);
		if (readBytes.Length != count) throw new ArgumentOutOfRangeException($"Expected {count} bytes, only read {readBytes.Length}.");
		return readBytes.ToArray(); //TODO: Replace with ReadOnlyMemory<byte> return
	}

	public void WriteByteArray(byte[] value)
	{
		if (value == null)
		{
			WriteLength(0);
			return;
		}

		WriteLength(value.Length);

		if (value.Length == 0) return;

		_writer.Write(value, 0, value.Length);
	}

	public byte[] ReadByteArray(bool slurp = false)
	{
		int len = ReadLength();
		byte[] bytes = ReadBytes(len, slurp);
		return bytes;
	}

	public void Write(ulong[] value)
	{
		if (value == null)
		{
			WriteLength(0);
			return;
		}

		WriteLength(value.Length);

		if (value.Length == 0) return;
		for (int i = 0; i < value.Length; i++)
		{
			ulong val = value[i];
			Write(val);
		}
	}

	public ulong[] ReadUlongs(bool slurp = false)
	{
		int len = ReadLength();
		ulong[] ulongs = new ulong[len];
		for (int i = 0; i < ulongs.Length; i++) ulongs[i] = ReadUlong();
		return ulongs;
	}

	public void Write(short value, bool bigEndian = false)
	{
		if (bigEndian) _writer.Write(BinaryPrimitives.ReverseEndianness(value));
		else _writer.Write(value);
	}

	public short ReadShort(bool bigEndian = false)
	{
		if (_reader.Position == _reader.Length) return 0;

		if (bigEndian) return BinaryPrimitives.ReverseEndianness(_reader.ReadInt16());

		return _reader.ReadInt16();
	}

	public void Write(ushort value, bool bigEndian = false)
	{
		if (bigEndian) _writer.Write(BinaryPrimitives.ReverseEndianness(value));
		else _writer.Write(value);
	}

	public ushort ReadUshort(bool bigEndian = false)
	{
		if (_reader.Position == _reader.Length) return 0;

		if (bigEndian) return BinaryPrimitives.ReverseEndianness(_reader.ReadUInt16());

		return _reader.ReadUInt16();
	}

	public void WriteBe(short value)
	{
		_writer.Write(BinaryPrimitives.ReverseEndianness(value));
	}

	public short ReadShortBe()
	{
		if (_reader.Position == _reader.Length) return 0;

		return BinaryPrimitives.ReverseEndianness(_reader.ReadInt16());
	}

	public void Write(Int24 value)
	{
		_writer.Write(value.GetBytes());
	}

	public Int24 ReadLittle()
	{
		return new Int24(_reader.Read(3).Span);
	}

	public void Write(int value, bool bigEndian = false)
	{
		if (bigEndian) _writer.Write(BinaryPrimitives.ReverseEndianness(value));
		else _writer.Write(value);
	}

	public int ReadInt(bool bigEndian = false)
	{
		if (bigEndian) return BinaryPrimitives.ReverseEndianness(_reader.ReadInt32());

		return _reader.ReadInt32();
	}

	public void WriteBe(int value)
	{
		_writer.Write(BinaryPrimitives.ReverseEndianness(value));
	}

	public int ReadIntBe()
	{
		return BinaryPrimitives.ReverseEndianness(_reader.ReadInt32());
	}

	public void Write(uint value)
	{
		_writer.Write(value);
	}

	public uint ReadUint()
	{
		return _reader.ReadUInt32();
	}


	public void WriteVarInt(int value)
	{
		VarInt.WriteInt32(_buffer, value);
	}

	public int ReadVarInt()
	{
		return VarInt.ReadInt32(_reader);
	}

	public void WriteSignedVarInt(int value)
	{
		VarInt.WriteSInt32(_buffer, value);
	}

	public int ReadSignedVarInt()
	{
		return VarInt.ReadSInt32(_reader);
	}

	public void WriteUnsignedVarInt(uint value)
	{
		VarInt.WriteUInt32(_buffer, value);
	}

	public uint ReadUnsignedVarInt()
	{
		return VarInt.ReadUInt32(_reader);
	}

	public int ReadLength()
	{
		return (int) VarInt.ReadUInt32(_reader);
	}

	public void WriteLength(int value)
	{
		VarInt.WriteUInt32(_buffer, (uint) value);
	}

	public void WriteVarLong(long value)
	{
		VarInt.WriteInt64(_buffer, value);
	}

	public long ReadVarLong()
	{
		return VarInt.ReadInt64(_reader);
	}

	public void WriteEntityId(long value)
	{
		WriteSignedVarLong(value);
	}

	public void WriteSignedVarLong(long value)
	{
		VarInt.WriteSInt64(_buffer, value);
	}

	public long ReadSignedVarLong()
	{
		return VarInt.ReadSInt64(_reader);
	}

	public void WriteRuntimeEntityId(long value)
	{
		WriteUnsignedVarLong(value);
	}

	public void WriteUnsignedVarLong(long value)
	{
		// Need to fix this to ulong later
		VarInt.WriteUInt64(_buffer, (ulong) value);
	}

	public long ReadUnsignedVarLong()
	{
		// Need to fix this to ulong later
		return (long) VarInt.ReadUInt64(_reader);
	}

	public void Write(long value)
	{
		_writer.Write(BinaryPrimitives.ReverseEndianness(value));
	}

	public long ReadLong()
	{
		return BinaryPrimitives.ReverseEndianness(_reader.ReadInt64());
	}

	public void Write(ulong value)
	{
		_writer.Write(value);
	}

	public ulong ReadUlong()
	{
		return _reader.ReadUInt64();
	}

	public void Write(float value)
	{
		_writer.Write(value);

		//byte[] bytes = BitConverter.GetBytes(value);
		//_writer.Write(bytes[3]);
		//_writer.Write(bytes[2]);
		//_writer.Write(bytes[1]);
		//_writer.Write(bytes[0]);
	}

	public float ReadFloat()
	{
		//byte[] buffer = _reader.ReadBytes(4);
		//return BitConverter.ToSingle(new[] {buffer[3], buffer[2], buffer[1], buffer[0]}, 0);
		return _reader.ReadSingle();
	}

	public void Write(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			WriteLength(0);
			return;
		}

		byte[] bytes = Encoding.UTF8.GetBytes(value);

		WriteLength(bytes.Length);
		Write(bytes);
	}

	public string ReadString()
	{
		if (_reader.Position == _reader.Length) return string.Empty;
		int len = ReadLength();
		if (len <= 0) return string.Empty;
		return Encoding.UTF8.GetString(ReadBytes(len));
	}

	public void WriteFixedString(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			Write((short) 0, true);
			return;
		}

		byte[] bytes = Encoding.UTF8.GetBytes(value);

		Write((short) bytes.Length, true);
		Write(bytes);
	}

	public string ReadFixedString()
	{
		if (_reader.Position == _reader.Length) return string.Empty;
		short len = ReadShort(true);
		if (len <= 0) return string.Empty;
		return Encoding.UTF8.GetString(_reader.Read(len).Span);
	}

	public void Write(Vector2 vec)
	{
		Write(vec.X);
		Write(vec.Y);
	}

	public Vector2 ReadVector2()
	{
		return new Vector2(ReadFloat(), ReadFloat());
	}

	public void Write(Vector3 vec)
	{
		Write(vec.X);
		Write(vec.Y);
		Write(vec.Z);
	}

	public Vector3 ReadVector3()
	{
		return new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
	}


	public void Write(BlockCoordinates coord)
	{
		WriteSignedVarInt(coord.X);
		WriteUnsignedVarInt((uint) coord.Y);
		WriteSignedVarInt(coord.Z);
	}

	public void WritePaintingCoordinates(BlockCoordinates coord)
	{
		Write((float) coord.X);
		Write((float) coord.Y);
		Write((float) coord.Z);
	}

	public BlockCoordinates ReadBlockCoordinates()
	{
		return new BlockCoordinates(ReadSignedVarInt(), (int) ReadUnsignedVarInt(), ReadSignedVarInt());
	}

	public void Write(PlayerRecords records)
	{
		if (records is PlayerAddRecords)
		{
			Write((byte) 0);
			WriteUnsignedVarInt((uint) records.Count);
			foreach (Player record in records)
			{
				Write(record.ClientUuid);
				WriteSignedVarLong(record.EntityId);
				Write(record.DisplayName ?? record.Username);
				Write(record.PlayerInfo.CertificateData?.ExtraData?.Xuid ?? String.Empty);
				Write(record.PlayerInfo.PlatformChatId);
				Write(record.PlayerInfo.DeviceOS);
				Write(record.Skin);
				Write(false); // is teacher
				Write(false); // is host
				Write(false); // subclient?
			}
		}
		else if (records is PlayerRemoveRecords)
		{
			Write((byte) 1);
			WriteUnsignedVarInt((uint) records.Count);
			foreach (Player record in records) Write(record.ClientUuid);
		}

		if (records is PlayerAddRecords)
			foreach (Player record in records)
				Write(true); // is verified
	}

	public PlayerRecords ReadPlayerRecords()
	{
		// This should never be used in production. It is primarily for 
		// the client to work.
		byte recordType = ReadByte();
		uint count = ReadUnsignedVarInt();
		PlayerRecords records = null;
		switch (recordType)
		{
			case 0:
				records = new PlayerAddRecords();
				for (int i = 0; i < count; i++)
				{
					var player = new Player(null, null);
					player.ClientUuid = ReadUUID();
					player.EntityId = ReadSignedVarLong();
					player.DisplayName = ReadString();
					string xuid = ReadString();
					string platformChatId = ReadString();
					int deviceOS = ReadInt();
					player.Skin = ReadSkin();
					ReadBool(); // is teacher
					ReadBool(); // is host
					ReadBool(); // is subclient

					player.PlayerInfo = new PlayerInfo
					{
						PlatformChatId = platformChatId,
						DeviceOS = deviceOS,
						CertificateData = new CertificateData { ExtraData = new ExtraData { Xuid = xuid } }
					};
					records.Add(player);
					//Log.Debug($"Reading {player.ClientUuid}, {player.EntityId}, '{player.DisplayName}', {platformChatId}");
				}
				break;
			case 1:
				records = new PlayerRemoveRecords();
				for (int i = 0; i < count; i++)
				{
					var player = new Player(null, null);
					player.ClientUuid = ReadUUID();
					records.Add(player);
				}
				break;
		}

		if (records is PlayerAddRecords)
			foreach (Player player in records)
			{
				bool isVerified = ReadBool();

				if (player.Skin != null)
					player.Skin.IsVerified = true;
			}
		//if (!_reader.Eof) ReadBool(); // damn BS
		//if (!_reader.Eof) ReadBool(); // damn BS

		return records;
	}

	public void Write(Records records)
	{
		WriteUnsignedVarInt((uint) records.Count);
		foreach (BlockCoordinates coord in records) Write(coord);
	}

	public Records ReadRecords()
	{
		var records = new Records();
		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			BlockCoordinates coord = ReadBlockCoordinates();
			records.Add(coord);
		}

		return records;
	}

	public void Write(PlayerLocation location)
	{
		Write(location.X);
		Write(location.Y);
		Write(location.Z);
		float d = 256f / 360f;
		Write((byte) Math.Round(location.Pitch * d)); // 256/360
		Write((byte) Math.Round(location.HeadYaw * d)); // 256/360
		Write((byte) Math.Round(location.Yaw * d)); // 256/360
	}

	public PlayerLocation ReadPlayerLocation()
	{
		var location = new PlayerLocation();
		location.X = ReadFloat();
		location.Y = ReadFloat();
		location.Z = ReadFloat();
		location.Pitch = ReadByte() * 1f / 0.71f;
		location.HeadYaw = ReadByte() * 1f / 0.71f;
		location.Yaw = ReadByte() * 1f / 0.71f;

		return location;
	}

	public void Write(IPEndPoint endpoint)
	{
		if (endpoint.AddressFamily == AddressFamily.InterNetwork)
		{
			Write((byte) 4);
			string[] parts = endpoint.Address.ToString().Split('.');
			foreach (string part in parts) Write((byte) ~byte.Parse(part));
			Write((short) endpoint.Port, true);
		}
	}


//typedef struct sockaddr_in6
//{
//	ADDRESS_FAMILY sin6_family; // AF_INET6.
//	USHORT sin6_port;           // Transport level port number.
//	ULONG sin6_flowinfo;       // IPv6 flow information.
//	IN6_ADDR sin6_addr;         // IPv6 address.
//	union {
//ULONG sin6_scope_id;     // Set of interfaces for a scope.
//	SCOPE_ID sin6_scope_struct;
//};
//}
//SOCKADDR_IN6_LH, * PSOCKADDR_IN6_LH, FAR * LPSOCKADDR_IN6_LH;

	public IPEndPoint ReadIPEndPoint()
	{
		byte ipVersion = ReadByte();

		IPAddress address = IPAddress.Any;
		int port = 0;

		if (ipVersion == 4)
		{
			string ipAddress = $"{(byte) ~ReadByte()}.{(byte) ~ReadByte()}.{(byte) ~ReadByte()}.{(byte) ~ReadByte()}";
			address = IPAddress.Parse(ipAddress);
			port = (ushort) ReadShort(true);
		}
		else if (ipVersion == 6)
		{
			ReadShort(); // Address family
			port = (ushort) ReadShort(true); // Port
			ReadLong(); // Flow info
			byte[] addressBytes = ReadBytes(16);
			address = new IPAddress(addressBytes);
		}
		else
			Log.Error($"Wrong IP version. Expected IPv4 or IPv6 but was IPv{ipVersion}");

		return new IPEndPoint(address, port);
	}

	public void Write(IPEndPoint[] endpoints)
	{
		foreach (IPEndPoint endpoint in endpoints) Write(endpoint);
	}

	public IPEndPoint[] ReadIPEndPoints(int count)
	{
		if (count == 20 && _reader.Length < 120) count = 10;
		var endPoints = new IPEndPoint[count];
		for (int i = 0; i < endPoints.Length; i++) endPoints[i] = ReadIPEndPoint();

		return endPoints;
	}

	public void Write(UUID uuid)
	{
		if (uuid == null) throw new Exception("Expected UUID, required");
		Write(uuid.GetBytes());
	}

	public UUID ReadUUID()
	{
		var uuid = new UUID(ReadBytes(16));
		return uuid;
	}

	public void Write(Nbt nbt)
	{
		Write(nbt, _writer.BaseStream, nbt.NbtFile.UseVarInt || this is McpeBlockEntityData || this is McpeUpdateEquipment);
	}

	public static void Write(Nbt nbt, Stream stream, bool useVarInt)
	{
		NbtFile file = nbt.NbtFile;
		file.BigEndian = false;
		file.UseVarInt = useVarInt;

		byte[] saveToBuffer = file.SaveToBuffer(NbtCompression.None);
		stream.Write(saveToBuffer, 0, saveToBuffer.Length);
	}


	public Nbt ReadNbt()
	{
		return ReadNbt(_reader);
	}

	public static Nbt ReadNbt(Stream stream, bool allowAlternativeRootTag = true, bool useVarInt = true)
	{
		var nbt = new Nbt();
		var nbtFile = new NbtFile();
		nbtFile.BigEndian = false;
		nbtFile.UseVarInt = useVarInt;
		nbtFile.AllowAlternativeRootTag = allowAlternativeRootTag;

		nbt.NbtFile = nbtFile;
		nbtFile.LoadFromStream(stream, NbtCompression.None);

		return nbt;
	}

	public static NbtCompound ReadNbtCompound(Stream stream, bool useVarInt = false)
	{
		var file = new NbtFile();
		file.BigEndian = false;
		file.UseVarInt = useVarInt;
		file.AllowAlternativeRootTag = false;

		file.LoadFromStream(stream, NbtCompression.None);

		return (NbtCompound) file.RootTag;
	}

	public void Write(MetadataInts metadata)
	{
		if (metadata == null)
		{
			WriteUnsignedVarInt(0);
			return;
		}

		WriteUnsignedVarInt((uint) metadata.Count);

		for (byte i = 0; i < metadata.Count; i++)
		{
			var slot = metadata[i] as MetadataInt;
			if (slot != null) WriteUnsignedVarInt((uint) slot.Value);
		}
	}

	public MetadataInts ReadMetadataInts()
	{
		var metadata = new MetadataInts();
		uint count = ReadUnsignedVarInt();

		for (byte i = 0; i < count; i++) metadata[i] = new MetadataInt((int) ReadUnsignedVarInt());

		return metadata;
	}

	public void Write(List<CreativeItemEntry> itemStacks)
	{
		WriteUnsignedVarInt((uint) itemStacks.Count);

		int netId = 0;
		foreach (CreativeItemEntry item in itemStacks)
		{
			item.Item.RuntimeId = (int) BlockFactory.GetItemRuntimeId(item.Item.Id, (byte)item.Item.Metadata);
			WriteUnsignedVarInt((uint) netId);
			Write(item.Item, false);
			WriteUnsignedVarInt(item.GroupIndex);
			netId++;
		}
	}

	public List<CreativeItemEntry> ReadCreativeItemStacks()
	{
		var metadata = new List<CreativeItemEntry>();

		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			uint networkId = ReadUnsignedVarInt();
			Item item = ReadItem(false);
			item.NetworkId = (int)networkId;
			uint groupIndex = ReadUnsignedVarInt();
			metadata.Add(new CreativeItemEntry(groupIndex, item));
		}

		return metadata;
	}
	
	public void Write(List<creativeGroup> groups)
	{
		WriteUnsignedVarInt((uint) groups.Count);

		foreach (creativeGroup group in groups)
		{
			Write(group.Category);
			Write(group.Name);
			Write(group.Icon, false);
		}
	}
	public List<creativeGroup> ReadCreativeGroups()
	{
		var group = new List<creativeGroup>();

		uint groupCount = ReadUnsignedVarInt();
		for (int i = 0; i < groupCount; i++)
		{
			int category = ReadInt();
			string name = ReadString();
			Item item = ReadItem(false);
			group.Add(new creativeGroup(category, name, item));
		}

		return group;
	}

	public void Write(ItemStacks itemStacks)
	{
		if (itemStacks == null)
		{
			WriteUnsignedVarInt(0);
			return;
		}

		WriteUnsignedVarInt((uint) itemStacks.Count);
		for (int i = 0; i < itemStacks.Count; i++) Write(itemStacks[i]);
	}

	public ItemStacks ReadItemStacks()
	{
		var metadata = new ItemStacks();

		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			int networkId = 0;
			if (this is McpeCreativeContent) networkId = ReadVarInt();
			Item item = ReadItem(this is not McpeCreativeContent);
			item.NetworkId = networkId;
			metadata.Add(item);
			//Log.Warn(item);
		}

		return metadata;
	}

	public void Write(Transaction transaction)
	{
		WriteSignedVarInt(transaction.RequestId);

		if (transaction.RequestId != 0)
		{
			WriteUnsignedVarInt((uint) transaction.RequestRecords.Count);

			foreach (RequestRecord record in transaction.RequestRecords)
			{
				Write(record.ContainerId);
				WriteUnsignedVarInt((uint) record.Slots.Count);

				foreach (byte slot in record.Slots) Write(slot);
			}
		}

		switch (transaction)
		{
			case InventoryMismatchTransaction _:
				WriteUnsignedVarInt((int) McpeInventoryTransaction.TransactionType.InventoryMismatch);
				break;
			case ItemReleaseTransaction _:
				WriteUnsignedVarInt((int) McpeInventoryTransaction.TransactionType.ItemRelease);
				break;
			case ItemUseOnEntityTransaction _:
				WriteUnsignedVarInt((int) McpeInventoryTransaction.TransactionType.ItemUseOnEntity);
				break;
			case ItemUseTransaction _:
				WriteUnsignedVarInt((int) McpeInventoryTransaction.TransactionType.ItemUse);
				break;
			case NormalTransaction _:
				WriteUnsignedVarInt((int) McpeInventoryTransaction.TransactionType.Normal);
				break;
		}
		//Write(transaction.HasNetworkIds);

		WriteUnsignedVarInt((uint) transaction.TransactionRecords.Count);
		foreach (TransactionRecord record in transaction.TransactionRecords)
		{
			switch (record)
			{
				case ContainerTransactionRecord r:
					WriteVarInt((int) McpeInventoryTransaction.InventorySourceType.Container);
					WriteSignedVarInt(r.InventoryId);
					break;
				case GlobalTransactionRecord _:
					WriteVarInt((int) McpeInventoryTransaction.InventorySourceType.Global);
					break;
				case WorldInteractionTransactionRecord r:
					WriteVarInt((int) McpeInventoryTransaction.InventorySourceType.WorldInteraction);
					WriteVarInt(r.Flags);
					break;
				case CreativeTransactionRecord _:
					WriteVarInt((int) McpeInventoryTransaction.InventorySourceType.Creative);
					break;
				case CraftTransactionRecord r:
					WriteVarInt((int) McpeInventoryTransaction.InventorySourceType.Crafting);
					WriteVarInt((int) r.Action);
					break;
			}

			WriteVarInt(record.Slot);
			Write(record.OldItem);
			Write(record.NewItem);

			//if (transaction.HasNetworkIds)
			//	WriteSignedVarInt(record.StackNetworkId);
		}

		switch (transaction)
		{
			case NormalTransaction _:
			case InventoryMismatchTransaction _:
				break;
			case ItemUseTransaction t:
				WriteUnsignedVarInt((uint) t.ActionType);
				WriteUnsignedVarInt((uint) t.TriggerType);
				Write(t.Position);
				WriteSignedVarInt(t.Face);
				WriteSignedVarInt(t.Slot);
				Write(t.Item);
				Write(t.FromPosition);
				Write(t.ClickPosition);
				WriteUnsignedVarInt(t.BlockRuntimeId);
				Write(t.ClientPredictedResult);
				break;
			case ItemUseOnEntityTransaction t:
				WriteUnsignedVarLong(t.EntityId);
				WriteUnsignedVarInt((uint) t.ActionType);
				WriteSignedVarInt(t.Slot);
				Write(t.Item);
				Write(t.FromPosition);
				Write(t.ClickPosition);
				break;
			case ItemReleaseTransaction t:
				WriteUnsignedVarInt((uint) t.ActionType);
				WriteSignedVarInt(t.Slot);
				Write(t.Item);
				Write(t.FromPosition);
				break;
		}
	}

	public Transaction ReadTransaction()
	{
		int requestId = ReadSignedVarInt(); // request id
		var requestRecords = new List<RequestRecord>();
		if (requestId != 0)
		{
			uint c1 = ReadUnsignedVarInt();
			for (int i = 0; i < c1; i++)
			{
				var rr = new RequestRecord();
				rr.ContainerId = ReadByte();
				uint c2 = ReadUnsignedVarInt();
				for (int j = 0; j < c2; j++)
				{
					byte slot = ReadByte();
					rr.Slots.Add(slot);
					//Log.Debug($"RequestId:{requestId}, containerId:{rr.ContainerId}, slot:{slot}");
				}
				requestRecords.Add(rr);
			}
		}

		var transactionType = (McpeInventoryTransaction.TransactionType) ReadVarInt();
		//bool hasItemStacks = ReadBool();
		//if(hasItemStacks) Log.Warn($"Got item stacks in old transaction");

		var transactions = new List<TransactionRecord>();
		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			TransactionRecord record;
			int sourceType = ReadVarInt();
			switch ((McpeInventoryTransaction.InventorySourceType) sourceType)
			{
				case McpeInventoryTransaction.InventorySourceType.Container:
					record = new ContainerTransactionRecord { InventoryId = ReadSignedVarInt() };
					break;
				case McpeInventoryTransaction.InventorySourceType.Global:
					record = new GlobalTransactionRecord();
					break;
				case McpeInventoryTransaction.InventorySourceType.WorldInteraction:
					record = new WorldInteractionTransactionRecord { Flags = ReadVarInt() };
					break;
				case McpeInventoryTransaction.InventorySourceType.Creative:
					record = new CreativeTransactionRecord { InventoryId = 0x79 };
					break;
				case McpeInventoryTransaction.InventorySourceType.Unspecified:
				case McpeInventoryTransaction.InventorySourceType.Crafting:
					record = new CraftTransactionRecord { Action = (McpeInventoryTransaction.CraftingAction) ReadSignedVarInt() };
					break;
				default:
					Log.Error($"Unknown inventory source type={sourceType}");
					continue;
			}

			record.Slot = ReadVarInt();
			record.OldItem = ReadItem();
			record.NewItem = ReadItem();
			//	if (hasItemStacks) 
			//	record.StackNetworkId = ReadSignedVarInt();

			transactions.Add(record);
		}

		Transaction transaction = null;
		switch (transactionType)
		{
			case McpeInventoryTransaction.TransactionType.Normal:
				transaction = new NormalTransaction();
				break;
			case McpeInventoryTransaction.TransactionType.InventoryMismatch:
				transaction = new InventoryMismatchTransaction();
				break;
			case McpeInventoryTransaction.TransactionType.ItemUse:
				transaction = new ItemUseTransaction
				{
					ActionType = (McpeInventoryTransaction.ItemUseAction) ReadVarInt(),
					TriggerType = (McpeInventoryTransaction.TriggerType) ReadVarInt(),
					Position = ReadBlockCoordinates(),
					Face = ReadSignedVarInt(),
					Slot = ReadSignedVarInt(),
					Item = ReadItem(),
					FromPosition = ReadVector3(),
					ClickPosition = ReadVector3(),
					BlockRuntimeId = ReadUnsignedVarInt(),
					ClientPredictedResult = ReadUnsignedVarInt()
				};
				break;
			case McpeInventoryTransaction.TransactionType.ItemUseOnEntity:
				transaction = new ItemUseOnEntityTransaction
				{
					EntityId = ReadVarLong(),
					ActionType = (McpeInventoryTransaction.ItemUseOnEntityAction) ReadVarInt(),
					Slot = ReadSignedVarInt(),
					Item = ReadItem(),
					FromPosition = ReadVector3(),
					ClickPosition = ReadVector3()
				};
				break;
			case McpeInventoryTransaction.TransactionType.ItemRelease:
				transaction = new ItemReleaseTransaction
				{
					ActionType = (McpeInventoryTransaction.ItemReleaseAction) ReadVarInt(),
					Slot = ReadSignedVarInt(),
					Item = ReadItem(),
					FromPosition = ReadVector3()
				};
				break;
		}

		transaction.TransactionRecords = transactions;
		transaction.RequestId = requestId;
		transaction.RequestRecords = requestRecords;

		return transaction;
	}

	public StackRequestSlotInfo ReadStackRequestSlotInfo()
	{
		FullContainerName containerName = readFullContainerName();
		byte slot = ReadByte();
		int stackNetworkId = ReadSignedVarInt();
		//Log.Warn("ContainerId | Slot | DynamicID | NetworkId");
		//Log.Warn($"{containerName.ContainerId} | {slot} | {containerName.DynamicId} | {stackNetworkId}");
		return new StackRequestSlotInfo
		{
			ContainerId = containerName.ContainerId,
			Slot = slot,
			StackNetworkId = stackNetworkId,
			DynamicId = containerName.DynamicId
		};
	}

	public FullContainerName readFullContainerName()
	{
		var name = new FullContainerName();
		name.ContainerId = ReadByte();
		name.DynamicId = ReadByte();
		return name;
	}

	public void Write(FullContainerName name)
	{
		Write(name.ContainerId);
		Write((byte) name.DynamicId);
	}

	public void Write(StackRequestSlotInfo slotInfo)
	{
		Write(new FullContainerName
		{
			ContainerId = slotInfo.ContainerId,
			DynamicId = slotInfo.DynamicId
		});
		Write(slotInfo.Slot);
		WriteSignedVarInt(slotInfo.StackNetworkId);
	}

	public void Write(ItemStackRequests requests)
	{
		WriteUnsignedVarInt((uint) requests.Count);

		foreach (ItemStackActionList request in requests)
		{
			WriteSignedVarInt(request.RequestId);
			WriteUnsignedVarInt((uint) request.Count);

			foreach (ItemStackAction action in request)
				switch (action)
				{
					case TakeAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Take);
						Write(ta.Count);
						Write(ta.Source);
						Write(ta.Destination);
						break;
					}

					case PlaceAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Place);
						Write(ta.Count);
						Write(ta.Source);
						Write(ta.Destination);
						break;
					}

					case SwapAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Swap);
						Write(ta.Source);
						Write(ta.Destination);
						break;
					}

					case DropAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Drop);
						Write(ta.Count);
						Write(ta.Source);
						Write(ta.Randomly);
						break;
					}

					case DestroyAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Destroy);
						Write(ta.Count);
						Write(ta.Source);
						break;
					}

					case ConsumeAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Consume);
						Write(ta.Count);
						Write(ta.Source);
						break;
					}

					case CreateAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.Create);
						Write(ta.ResultSlot);
						break;
					}

					case PlaceIntoBundleAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.PlaceIntoBundleDeprecated);
						break;
					}

					case TakeFromBundleAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.TakeFromBundleDeprecated);
						break;
					}

					case LabTableCombineAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.LabTableCombine);
						break;
					}

					case BeaconPaymentAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.BeaconPayment);
						WriteSignedVarInt(ta.PrimaryEffect);
						WriteSignedVarInt(ta.SecondaryEffect);
						break;
					}

					case CraftAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftRecipe);
						WriteUnsignedVarInt(ta.RecipeNetworkId);
						Write(ta.TimesCrafted);
						break;
					}

					case CraftAutoAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftRecipeAuto);
						WriteUnsignedVarInt(ta.RecipeNetworkId);
						Write(ta.TimesCrafted2);
						Write(ta.TimesCrafted);
						Write((byte) ta.Ingredients.Count);
						foreach (Item item in ta.Ingredients) WriteRecipeIngredient(item);
						break;
					}

					case CraftCreativeAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftCreative);
						WriteUnsignedVarInt(ta.CreativeItemNetworkId);
						Write(ta.ClientPredictedResult);
						break;
					}

					case CraftRecipeOptionalAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftRecipeOptional);
						WriteUnsignedVarInt(ta.RecipeNetworkId);
						Write(ta.FilteredStringIndex);
						break;
					}

					case GrindstoneStackRequestAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftGrindstone);
						WriteUnsignedVarInt(ta.RecipeNetworkId);
						WriteVarInt(ta.RepairCost);
						Write(ta.TimesCrafted);
						break;
					}

					case LoomStackRequestAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftLoom);
						Write(ta.PatternId);
						Write(ta.TimesCrafted);
						break;
					}

					case CraftNotImplementedDeprecatedAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftNotImplementedDeprecated);
						break;
					}

					case CraftResultDeprecatedAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.CraftResultsDeprecated);
						Write(ta.ResultItems);
						Write(ta.TimesCrafted);
						break;
					}

					case MineBlockAction ta:
					{
						Write((byte) McpeItemStackRequest.ActionType.MineBlock);
						WriteVarInt(ta.Slot);
						WriteVarInt(ta.Durability);
						WriteSignedVarInt(ta.stackNetworkId);
						break;
					}
				}

			WriteUnsignedVarInt((uint) request.filteredString.Count);

			for (int fi = 0; fi < request.filteredString.Count; fi++) Write(request.filteredString[fi]);
		}
	}

	//public const TAKE = 0;
	//public const PLACE = 1;
	//public const SWAP = 2;
	//public const DROP = 3;
	//public const DESTROY = 4;
	//public const CRAFTING_CONSUME_INPUT = 5;
	//public const CRAFTING_MARK_SECONDARY_RESULT_SLOT = 6;
	//public const LAB_TABLE_COMBINE = 7;
	//public const BEACON_PAYMENT = 8;
	//public const CRAFTING_RECIPE = 9;
	//public const CRAFTING_RECIPE_AUTO = 10; //recipe book?
	//public const CREATIVE_CREATE = 11;
	//public const CRAFT_RECIPE_OPTIONAL = 12;
	//public const CRAFTING_NON_IMPLEMENTED_DEPRECATED_ASK_TY_LAING = 13; 
	//public const CRAFTING_RESULTS_DEPRECATED_ASK_TY_LAING = 14; //no idea what this is for

	public ItemStackRequests ReadItemStackRequests(bool single = false)
	{
		var requests = new ItemStackRequests();

		uint c = 1;

		if (!single) c = ReadUnsignedVarInt();

		//Log.Warn($"Count: {c}");
		for (int i = 0; i < c; i++)
		{
			var actions = new ItemStackActionList();
			actions.RequestId = ReadSignedVarInt();
			//Log.Warn($"Request ID: {actions.RequestId}");

			uint count = ReadUnsignedVarInt();
			//Log.Warn($"Count: {count}");
			for (int j = 0; j < count; j++)
			{
				var actionType = (McpeItemStackRequest.ActionType) ReadByte();
				//Log.Warn($"Action type: {actionType}");
				switch (actionType)
				{
					case McpeItemStackRequest.ActionType.Take:
					{
						var action = new TakeAction();
						action.Count = ReadByte();
						action.Source = ReadStackRequestSlotInfo();
						action.Destination = ReadStackRequestSlotInfo();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Place:
					{
						var action = new PlaceAction();
						action.Count = ReadByte();
						action.Source = ReadStackRequestSlotInfo();
						action.Destination = ReadStackRequestSlotInfo();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Swap:
					{
						var action = new SwapAction();
						action.Source = ReadStackRequestSlotInfo();
						action.Destination = ReadStackRequestSlotInfo();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Drop:
					{
						var action = new DropAction();
						action.Count = ReadByte();
						action.Source = ReadStackRequestSlotInfo();
						action.Randomly = ReadBool();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Destroy:
					{
						var action = new DestroyAction();
						action.Count = ReadByte();
						action.Source = ReadStackRequestSlotInfo();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Consume:
					{
						var action = new ConsumeAction();
						action.Count = ReadByte();
						action.Source = ReadStackRequestSlotInfo();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.Create:
					{
						var action = new CreateAction();
						action.ResultSlot = ReadByte();
						actions.Add(action);
						break;
					}

					case McpeItemStackRequest.ActionType.PlaceIntoBundleDeprecated:
					{
						var action = new PlaceIntoBundleAction();
						actions.Add(action);
						break;
					}

					case McpeItemStackRequest.ActionType.TakeFromBundleDeprecated:
					{
						var action = new TakeFromBundleAction();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.LabTableCombine:
					{
						var action = new LabTableCombineAction();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.BeaconPayment:
					{
						var action = new BeaconPaymentAction();
						action.PrimaryEffect = ReadSignedVarInt();
						action.SecondaryEffect = ReadSignedVarInt();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftRecipe:
					{
						var action = new CraftAction();
						action.RecipeNetworkId = ReadUnsignedVarInt();
						action.TimesCrafted = ReadByte();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftRecipeAuto:
					{
						var action = new CraftAutoAction();
						action.RecipeNetworkId = ReadUnsignedVarInt();
						action.TimesCrafted2 = ReadByte();
						action.TimesCrafted = ReadByte();
						byte cou = ReadByte();
						for (int a = 0; a < cou; a++) action.Ingredients.Add(ReadRecipeData());
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftCreative:
					{
						var action = new CraftCreativeAction();
						action.CreativeItemNetworkId = ReadUnsignedVarInt();
						action.ClientPredictedResult = ReadByte();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftRecipeOptional:
					{
						var action = new CraftRecipeOptionalAction();
						action.RecipeNetworkId = ReadUnsignedVarInt();
						action.FilteredStringIndex = ReadInt();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftGrindstone:
					{
						var action = new GrindstoneStackRequestAction();
						action.RecipeNetworkId = ReadUnsignedVarInt();
						action.RepairCost = ReadVarInt();
						action.TimesCrafted = ReadByte();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftLoom:
					{
						var action = new LoomStackRequestAction();
						action.PatternId = ReadString();
						action.TimesCrafted = ReadByte();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftNotImplementedDeprecated:
					{
						var action = new CraftNotImplementedDeprecatedAction();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.CraftResultsDeprecated:
					{
						var action = new CraftResultDeprecatedAction();
						action.ResultItems = ReadItems();
						action.TimesCrafted = ReadByte();
						actions.Add(action);
						break;
					}
					case McpeItemStackRequest.ActionType.MineBlock:
					{
						var action = new MineBlockAction();
						action.Slot = ReadVarInt();
						action.Durability = ReadVarInt();
						action.stackNetworkId = ReadSignedVarInt();
						actions.Add(action);
						break;
					}
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			requests.Add(actions);

			uint filterStringCount = ReadUnsignedVarInt();

			for (int fi = 0; fi < filterStringCount; fi++) actions.filteredString.Add(ReadString());
			uint filterStringCause = ReadUint();
		}

		return requests;
	}

	public void Write(ItemStackResponses responses)
	{
		WriteUnsignedVarInt((uint) responses.Count);
		foreach (ItemStackResponse stackResponse in responses)
		{
			Write((byte) stackResponse.Result);
			WriteSignedVarInt(stackResponse.RequestId);
			if (stackResponse.Result != StackResponseStatus.Ok)
				continue;
			WriteUnsignedVarInt((uint) stackResponse.ResponseContainerInfos.Count);
			foreach (StackResponseContainerInfo containerInfo in stackResponse.ResponseContainerInfos)
			{
				Write(new FullContainerName
				{
					ContainerId = containerInfo.ContainerId,
					DynamicId = containerInfo.DynamicId
				});
				WriteUnsignedVarInt((uint) containerInfo.Slots.Count);
				foreach (StackResponseSlotInfo slot in containerInfo.Slots)
				{
					Write(slot.Slot);
					Write(slot.HotbarSlot);
					Write(slot.Count);
					WriteSignedVarInt(slot.StackNetworkId);
					Write(slot.CustomName);
					Write(slot.FilteredCustomName);
					WriteSignedVarInt(slot.DurabilityCorrection);
				}
			}
		}
	}


	public ItemStackResponses ReadItemStackResponses()
	{
		var responses = new ItemStackResponses();
		uint count = ReadUnsignedVarInt();

		for (int i = 0; i < count; i++)
		{
			var response = new ItemStackResponse();
			response.Result = (StackResponseStatus) ReadByte();
			response.RequestId = ReadSignedVarInt();

			if (response.Result != StackResponseStatus.Ok)
				continue;

			response.ResponseContainerInfos = new List<StackResponseContainerInfo>();
			uint subCount = ReadUnsignedVarInt();
			for (int sub = 0; sub < subCount; sub++)
			{
				var containerInfo = new StackResponseContainerInfo();
				FullContainerName name = readFullContainerName();
				containerInfo.ContainerId = name.ContainerId;
				containerInfo.DynamicId = name.DynamicId;
				uint slotCount = ReadUnsignedVarInt();
				containerInfo.Slots = new List<StackResponseSlotInfo>();

				for (int si = 0; si < slotCount; si++)
				{
					var slot = new StackResponseSlotInfo();
					slot.Slot = ReadByte();
					slot.HotbarSlot = ReadByte();
					slot.Count = ReadByte();
					slot.StackNetworkId = ReadSignedVarInt();
					slot.CustomName = ReadString();
					slot.FilteredCustomName = ReadString();
					slot.DurabilityCorrection = ReadSignedVarInt();

					containerInfo.Slots.Add(slot);
				}

				response.ResponseContainerInfos.Add(containerInfo);
			}

			responses.Add(response);
		}

		return responses;
	}

	public void Write(EnchantOptions options)
	{
		WriteUnsignedVarInt((uint) options.Count);
		foreach (EnchantOption option in options)
		{
			WriteUnsignedVarInt(option.Cost);
			Write(option.Flags);
			WriteEnchants(option.EquipActivatedEnchantments);
			WriteEnchants(option.HeldActivatedEnchantments);
			WriteEnchants(option.SelfActivatedEnchantments);
			Write(option.Name);
			WriteVarInt(option.OptionId);
		}
	}

	private void WriteEnchants(List<Enchant> enchants)
	{
		WriteUnsignedVarInt((uint) enchants.Count);
		foreach (Enchant enchant in enchants)
		{
			Write(enchant.Id);
			Write(enchant.Level);
		}
	}

	private List<Enchant> ReadEnchants()
	{
		var enchants = new List<Enchant>();
		uint count = ReadUnsignedVarInt();

		for (int i = 0; i < count; i++)
		{
			var enchant = new Enchant(ReadByte(), ReadByte());
			enchants.Add(enchant);
		}

		return enchants;
	}

	public EnchantOptions ReadEnchantOptions()
	{
		var options = new EnchantOptions();
		uint count = ReadUnsignedVarInt();

		for (int i = 0; i < count; i++)
		{
			var option = new EnchantOption();
			option.Cost = ReadUnsignedVarInt();
			option.Flags = ReadInt();
			option.EquipActivatedEnchantments = ReadEnchants();
			option.HeldActivatedEnchantments = ReadEnchants();
			option.SelfActivatedEnchantments = ReadEnchants();
			option.Name = ReadString();
			option.OptionId = ReadVarInt();

			options.Add(option);
		}

		return options;
	}

	public void Write(AnimationKey[] keys)
	{
		WriteUnsignedVarInt((uint) keys.Length);
		foreach (AnimationKey key in keys)
		{
			Write(key.ExecuteImmediate);
			Write(key.ResetBefore);
			Write(key.ResetAfter);
			Write(key.StartRotation);
			Write(key.EndRotation);
			WriteUnsignedVarInt(key.Duration);
		}
	}

	public AnimationKey[] ReadAnimationKeys()
	{
		uint count = ReadUnsignedVarInt();
		var keys = new AnimationKey[count];
		for (int i = 0; i < count; i++)
		{
			var key = new AnimationKey();
			key.ExecuteImmediate = ReadBool();
			key.ResetBefore = ReadBool();
			key.ResetAfter = ReadBool();
			key.StartRotation = ReadVector3();
			key.EndRotation = ReadVector3();
			key.Duration = ReadUnsignedVarInt();
			keys[i] = key;
		}

		return keys;
	}


	private ItemStacks ReadItems()
	{
		var items = new ItemStacks();

		uint count = ReadUnsignedVarInt();

		for (int i = 0; i < count; i++) items.Add(ReadItem(false));

		return items;
	}

	public void Write(Item stack, bool writeUniqueId = true)
	{
		if (stack == null || stack.Id == 0 || !ItemFactory.Translator.TryGetNetworkId(stack.Id, stack.Metadata, out TranslatedItem netData))
		{
			WriteSignedVarInt(0);
			return;
		}

		WriteSignedVarInt(netData.Id);
		Write((short) stack.Count);
		WriteUnsignedVarInt((uint) netData.Meta);

		if (writeUniqueId)
		{
			Write(stack.UniqueId != 0);

			if (stack.UniqueId != 0) WriteVarInt(stack.UniqueId);
		}

		WriteSignedVarInt(stack.RuntimeId);

		byte[] extraData = null;
		//Write extra data
		using (var ms = new MemoryStream())
		{
			using (var binaryWriter = new BinaryWriter(ms, Encoding.UTF8, true))
			{
				if (stack.ExtraData != null)
				{
					binaryWriter.Write((ushort) 0xffff);
					binaryWriter.Write((byte) 1);
					byte[] nbtData = GetNbtData(stack.ExtraData, false);
					binaryWriter.Write(nbtData);
				}
				else
					binaryWriter.Write((short) 0);

				binaryWriter.Write(0); //Write Int
				binaryWriter.Write(0); //Write Int

				if (stack.Id == 513) binaryWriter.Write((long) 0);
			}

			extraData = ms.ToArray();
		}

		WriteLength(extraData.Length);
		Write(extraData);
	}

	public Item ReadItem(bool readUniqueId = true)
	{
		int id = ReadSignedVarInt();
		if (id == 0) return new ItemAir();

		short count = ReadShort();
		uint metadata = ReadUnsignedVarInt();
		TranslatedItem translated = ItemFactory.Translator.FromNetworkId(id, (short) metadata);

		Item stack = ItemFactory.GetItem((short) translated.Id, translated.Meta, count);

		if (readUniqueId)
			if (ReadBool())
				stack.UniqueId = ReadVarInt();

		stack.RuntimeId = ReadSignedVarInt();

		int length = ReadLength();
		byte[] data = ReadBytes(length);

		using (var ms = new MemoryStream(data))
		using (var binaryReader = new BinaryReader(ms))
		{
			ushort nbtLen = binaryReader.ReadUInt16();
			if (nbtLen == 0xffff)
			{
				byte version = binaryReader.ReadByte();

				if (version != 1) throw new Exception($"Fringe nbt version when reading item extra NBT: {version}");

				long beforeRead = ms.Position;
				stack.ExtraData = ReadNbtCompound(ms);
				long afterRead = ms.Position;
				long nbtCompoundLength = afterRead - beforeRead;
			}
			else if (nbtLen > 0) throw new Exception($"Fringe nbt length when reading item extra NBT: {nbtLen}");

			int canPlace = binaryReader.ReadInt32();
			for (int i = 0; i < canPlace; i++)
			{
				short l = binaryReader.ReadInt16();
				binaryReader.ReadBytes(l);
			}
			int canBreak = binaryReader.ReadInt32();
			for (int i = 0; i < canBreak; i++)
			{
				short l = binaryReader.ReadInt16();
				binaryReader.ReadBytes(l);
			}

			if (stack.RuntimeId == ShieldId) // shield
				binaryReader.ReadInt64(); // something about tick, crap code
		}
		return stack;
	}


	public static byte[] GetNbtData(NbtCompound nbtCompound, bool useVarInt = true)
	{
		nbtCompound.Name = string.Empty;
		var file = new NbtFile(nbtCompound);
		file.BigEndian = false;
		file.UseVarInt = useVarInt;

		return file.SaveToBuffer(NbtCompression.None);
	}

	public void Write(MetadataDictionary metadata)
	{
		if (metadata != null) metadata.WriteTo(_writer);
	}

	public MetadataDictionary ReadMetadataDictionary()
	{
		//_buffer.Position = _reader.Position;
		var reader = new BinaryReader(_reader);
		var dictionary = MetadataDictionary.FromStream(reader);
		//_reader.Position = (int) _buffer.Position;
		return dictionary;
	}

	public AttributeModifiers ReadAttributeModifiers()
	{
		var modifiers = new AttributeModifiers();
		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			var modifier = new AttributeModifier
			{
				Id = ReadString(),
				Name = ReadString(),
				Amount = ReadFloat(),
				Operations = ReadInt(),
				Operand = ReadInt(),
				Serializable = ReadBool()
			};
			modifiers[modifier.Name] = modifier;
		}

		return modifiers;
	}

	public void Write(AttributeModifiers modifiers)
	{
		WriteUnsignedVarInt((uint) modifiers.Count);
		foreach (AttributeModifier modifier in modifiers.Values)
		{
			Write(modifier.Id);
			Write(modifier.Name);
			Write(modifier.Amount);
			Write(modifier.Operations); // unknown
			Write(modifier.Operand);
			Write(modifier.Serializable);
		}
	}

	public PlayerAttributes ReadPlayerAttributes()
	{
		var attributes = new PlayerAttributes();
		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			var attribute = new PlayerAttribute
			{
				MinValue = ReadFloat(),
				MaxValue = ReadFloat(),
				Value = ReadFloat(),
				DefaultMinValue = ReadFloat(),
				DefaultMaxValue = ReadFloat(),
				Default = ReadFloat(),
				Name = ReadString(),
				Modifiers = ReadAttributeModifiers()
			};
			attributes[attribute.Name] = attribute;
		}

		return attributes;
	}

	public void Write(PlayerAttributes attributes)
	{
		WriteUnsignedVarInt((uint) attributes.Count);
		foreach (PlayerAttribute attribute in attributes.Values)
		{
			Write(attribute.MinValue);
			Write(attribute.MaxValue);
			Write(attribute.Value);
			Write(attribute.DefaultMinValue == 0.0f ? attribute.MinValue : attribute.DefaultMinValue); //hack to not to break plugins
			Write(attribute.DefaultMaxValue == 0.0f ? attribute.MaxValue : attribute.DefaultMaxValue); //here too
			Write(attribute.Default); // unknown
			Write(attribute.Name);
			Write(attribute.Modifiers);
		}
	}


	public GameRules ReadGameRules()
	{
		var gameRules = new GameRules();

		int count = ReadVarInt();
		for (int i = 0; i < count; i++)
		{
			string name = ReadString();
			bool isPlayerModifiable = ReadBool();
			uint type = ReadUnsignedVarInt();
			switch (type)
			{
				case 1:
				{
					var rule = new GameRule<bool>(name, ReadBool()) { IsPlayerModifiable = isPlayerModifiable };
					gameRules.Add(rule);
					break;
				}
				case 2:
				{
					var rule = new GameRule<int>(name, ReadVarInt()) { IsPlayerModifiable = isPlayerModifiable };
					gameRules.Add(rule);
					break;
				}
				case 3:
				{
					var rule = new GameRule<float>(name, ReadFloat()) { IsPlayerModifiable = isPlayerModifiable };
					gameRules.Add(rule);
					break;
				}
			}
		}

		return gameRules;
	}

	public void Write(GameRules gameRules)
	{
		if (gameRules == null)
		{
			WriteVarInt(0);
			return;
		}

		WriteVarInt(gameRules.Count);
		foreach (GameRule rule in gameRules)
		{
			Write(rule.Name.ToLower());
			Write(rule.IsPlayerModifiable); // bool isPlayerModifiable

			if (rule is GameRule<bool>)
			{
				WriteUnsignedVarInt(1);
				Write(((GameRule<bool>) rule).Value);
			}
			else if (rule is GameRule<int>)
			{
				WriteUnsignedVarInt(2);
				WriteVarInt(((GameRule<int>) rule).Value);
			}
			else if (rule is GameRule<float>)
			{
				WriteUnsignedVarInt(3);
				Write(((GameRule<float>) rule).Value);
			}
		}
	}

	public void Write(EntityAttributes attributes)
	{
		if (attributes == null)
		{
			WriteUnsignedVarInt(0);
			return;
		}

		WriteUnsignedVarInt((uint) attributes.Count);
		foreach (EntityAttribute attribute in attributes.Values)
		{
			Write(attribute.Name);
			Write(attribute.MinValue);
			Write(attribute.Value);
			Write(attribute.MaxValue);
		}
	}

	public EntityAttributes ReadEntityAttributes()
	{
		var attributes = new EntityAttributes();
		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			var attribute = new EntityAttribute
			{
				Name = ReadString(),
				MinValue = ReadFloat(),
				Value = ReadFloat(),
				MaxValue = ReadFloat()
			};

			attributes[attribute.Name] = attribute;
		}

		return attributes;
	}

	public Itemstates ReadItemstates()
	{
		var result = new Itemstates();
		uint count = ReadUnsignedVarInt();
		for (int runtimeId = 0; runtimeId < count; runtimeId++)
		{
			string name = ReadString();
			short legacyId = ReadShort();
			bool component = ReadBool();
			int version = ReadVarInt();
			Nbt components = ReadNbt();
			
			byte[] componentValue = [];

			if (component && components.NbtFile.RootTag["components"] != null)
			{
				using var stream = new MemoryStream();
				var file = new NbtFile((components.NbtFile.RootTag["components"] as NbtCompound)!);
				file.SaveToStream(stream, NbtCompression.None);
				componentValue = stream.ToArray();
			}
			
			
			result.Add(new Itemstate
			{
				Id = legacyId,
				Name = name,
				ComponentBased = component,
				Version = version,
				Components = componentValue
			});
		}

		string fileNameItemstates = "newResources/itemstates.json";
		File.WriteAllText(fileNameItemstates, JsonConvert.SerializeObject(result, Formatting.Indented));
		Log.Warn("Received item runtime ids exported to newResources/itemstates.json\n");
		return result;
	}

	public void Write(Itemstates itemstates)
	{
		if (itemstates == null)
		{
			WriteUnsignedVarInt(0);
			return;
		}
		WriteUnsignedVarInt((uint) itemstates.Count);
		foreach (Itemstate itemstate in itemstates)
		{
			Write(itemstate.Name);
			Write(itemstate.Id);
			Write(itemstate.ComponentBased);
			WriteVarInt(itemstate.Version);
			var nbt = new Nbt
			{
				NbtFile = new NbtFile
				{
					BigEndian = false,
					UseVarInt = true,
					RootTag = new NbtCompound("")
				}
			};
			if (itemstate.ComponentBased && itemstate.Components.Length != 0)
			{
				using var stream = new MemoryStream(itemstate.Components);
				var file = new NbtFile();
				file.LoadFromStream(stream, NbtCompression.None);
				var componentNbt = new NbtCompound("")
				{
					(file.RootTag as NbtCompound)!
				};
				nbt.NbtFile.RootTag = componentNbt;
			}
			Write(nbt);
		}
	}

	public BlockPalette ReadBlockPalette()
	{
		var result = new BlockPalette();
		uint count = ReadUnsignedVarInt();

		for (int runtimeId = 0; runtimeId < count; runtimeId++)
		{
			var record = new BlockStateContainer();
			record.Id = record.RuntimeId = runtimeId;
			record.Name = ReadString();
			record.States = new List<IBlockState>();

			Nbt nbt = ReadNbt(_reader);
			NbtTag rootTag = nbt.NbtFile.RootTag;

			foreach (IBlockState state in GetBlockStates(rootTag)) record.States.Add(state);
		}

		return result;
	}

	private IEnumerable<IBlockState> GetBlockStates(NbtTag tag)
	{
		switch (tag.TagType)
		{
			case NbtTagType.List:
			{
				foreach (IBlockState state in GetBlockStatesFromList((NbtList) tag))
					yield return state;
			}
				break;

			case NbtTagType.Compound:
			{
				foreach (IBlockState state in GetBlockStatesFromCompound((NbtCompound) tag))
					yield return state;
			}
				break;

			default:
			{
				if (TryGetStateFromTag(tag, out IBlockState state))
					yield return state;
			}
				break;
		}
	}

	private IEnumerable<IBlockState> GetBlockStatesFromCompound(NbtCompound list)
	{
		if (list.TryGet("states", out NbtTag states))
			foreach (IBlockState state in GetBlockStates(states))
				yield return state;
	}


	private IEnumerable<IBlockState> GetBlockStatesFromList(NbtList list)
	{
		foreach (NbtTag tag in list)
			if (TryGetStateFromTag(tag, out IBlockState state))
				yield return state;
			else
				foreach (IBlockState s in GetBlockStates(tag))
					yield return s;
	}

	private bool TryGetStateFromTag(NbtTag tag, out IBlockState state)
	{
		switch (tag.TagType)
		{
			case NbtTagType.Byte:
				state = new BlockStateByte
				{
					Name = tag.Name,
					Value = tag.ByteValue
				};
				return true;

			case NbtTagType.Int:
				state = new BlockStateInt
				{
					Name = tag.Name,
					Value = tag.IntValue
				};
				return true;

			case NbtTagType.String:
				state = new BlockStateString
				{
					Name = tag.Name,
					Value = tag.StringValue
				};
				return true;
		}

		state = null;

		return false;
	}

	public void Write(BlockPalette palette)
	{
		if (palette == null)
		{
			WriteUnsignedVarInt(0);
			return;
		}
		WriteUnsignedVarInt((uint) palette.Count);
		foreach (BlockStateContainer record in palette.Values)
		{
			Write(record.Name);
			Write(record.StatesCacheNbt);
		}
	}

	public void Write(AbilityLayer layer)
	{
		Write((ushort) layer.Type);
		Write((uint) layer.Abilities);
		Write(layer.Values);
		Write(layer.FlySpeed);
		Write(layer.WalkSpeed);
		Write(layer.VerticalFlySpeed);
	}

	public AbilityLayer ReadAbilityLayer()
	{
		var layer = new AbilityLayer();
		layer.Type = (AbilityLayerType) ReadUshort();
		layer.Abilities = (PlayerAbility) ReadUint();
		layer.Values = ReadUint();
		layer.FlySpeed = ReadFloat();
		layer.WalkSpeed = ReadFloat();
		layer.VerticalFlySpeed = ReadFloat();

		return layer;
	}

	public void Write(AbilityLayers layers)
	{
		Write((byte) layers.Count);

		foreach (AbilityLayer layer in layers) Write(layer);
	}

	public AbilityLayers ReadAbilityLayers()
	{
		var layers = new AbilityLayers();
		byte count = ReadByte();

		for (int i = 0; i < count; i++) layers.Add(ReadAbilityLayer());
		return layers;
	}

	public void Write(EntityLink link)
	{
		WriteVarLong(link.FromEntityId);
		WriteVarLong(link.ToEntityId);
		Write((byte) link.Type);
		Write(link.Immediate);
		Write(link.CausedByRider);
		Write(link.VehicleAngularVelocity);
	}

	public EntityLink ReadEntityLink()
	{
		long from = ReadVarLong();
		long to = ReadVarLong();
		var type = (EntityLink.EntityLinkType) ReadByte();
		bool immediate = ReadBool();
		bool causedByRider = ReadBool();
		float vehicleAngularVelocity = ReadFloat();

		return new EntityLink(from, to, type, immediate, causedByRider, vehicleAngularVelocity);
	}

	public void Write(EntityLinks links)
	{
		if (links == null)
		{
			WriteUnsignedVarInt(0); // LE
			return;
		}
		WriteUnsignedVarInt((uint) links.Count); // LE
		foreach (EntityLink link in links) Write(link);
	}

	public EntityLinks ReadEntityLinks()
	{
		uint count = ReadUnsignedVarInt();

		var links = new EntityLinks();
		for (int i = 0; i < count; i++) links.Add(ReadEntityLink());

		return links;
	}

	public void Write(Rules rules)
	{
		_writer.Write(rules.Count); // LE
		foreach (RuleData rule in rules)
		{
			Write(rule.Name);
			Write(rule.Unknown1);
			Write(rule.Unknown2);
		}
	}

	public Rules ReadRules()
	{
		int count = _reader.ReadInt32(); // LE

		var rules = new Rules();
		for (int i = 0; i < count; i++)
		{
			var rule = new RuleData();
			rule.Name = ReadString();
			rule.Unknown1 = ReadBool();
			rule.Unknown2 = ReadBool();
			rules.Add(rule);
		}

		return rules;
	}

	public void Write(TexturePackInfos packInfos)
	{
		if (packInfos == null)
		{
			_writer.Write((short) 0);

			return;
		}

		_writer.Write((short) packInfos.Count); // LE
		//WriteVarInt(packInfos.Count);
		foreach (TexturePackInfo info in packInfos)
		{
			Write(info.UUID);
			Write(info.Version);
			Write(info.Size);
			Write(info.ContentKey);
			Write(info.SubPackName);
			Write(info.ContentIdentity);
			Write(info.HasScripts);
			Write(info.isAddon);
			Write(info.RtxEnabled);
			Write(info.cndUrls);
		}
	}

	public TexturePackInfos ReadTexturePackInfos()
	{
		int count = _reader.ReadInt16(); // LE
		//int count = ReadVarInt(); // LE

		var packInfos = new TexturePackInfos();
		for (int i = 0; i < count; i++)
		{
			var info = new TexturePackInfo();
			UUID id = ReadUUID();
			string version = ReadString();
			ulong size = ReadUlong();
			string encryptionKey = ReadString();
			string subpackName = ReadString();
			string contentIdentity = ReadString();
			bool hasScripts = ReadBool();
			bool isAddon = ReadBool();
			bool rtxEnabled = ReadBool();
			string cndUrls = ReadString();


			info.UUID = id;
			info.Version = version;
			info.Size = size;
			info.HasScripts = hasScripts;
			info.ContentKey = encryptionKey;
			info.SubPackName = subpackName;
			info.ContentIdentity = contentIdentity;
			info.isAddon = isAddon;
			info.RtxEnabled = rtxEnabled;
			info.cndUrls = cndUrls;

			packInfos.Add(info);
		}

		return packInfos;
	}

	public void Write(ResourcePackInfos packInfos)
	{
		if (packInfos == null)
		{
			_writer.Write((short) 0);
			return;
		}

		_writer.Write((short) packInfos.Count); // LE
		//WriteVarInt(packInfos.Count);
		foreach (ResourcePackInfo info in packInfos)
		{
			Write(info.UUID);
			Write(info.Version);
			Write(info.Size);
			Write(info.ContentKey);
			Write(info.SubPackName);
			Write(info.ContentIdentity);
			Write(info.HasScripts);
			Write(info.isAddon);
		}
	}

	public ResourcePackInfos ReadResourcePackInfos()
	{
		int count = _reader.ReadInt16(); // LE
		//int count = ReadVarInt(); // LE

		var packInfos = new ResourcePackInfos();
		for (int i = 0; i < count; i++)
		{
			var info = new ResourcePackInfo();

			UUID id = ReadUUID();
			string version = ReadString();
			ulong size = ReadUlong();
			string encryptionKey = ReadString();
			string subpackName = ReadString();
			string contentIdentity = ReadString();
			bool hasScripts = ReadBool();
			bool isAddon = ReadBool();

			info.UUID = id;
			info.Version = version;
			info.Size = size;
			info.ContentKey = encryptionKey;
			info.SubPackName = subpackName;
			info.ContentIdentity = contentIdentity;
			info.HasScripts = hasScripts;
			info.isAddon = isAddon;

			packInfos.Add(info);
		}

		return packInfos;
	}

	public void Write(ResourcePackIdVersions packInfos)
	{
		if (packInfos == null || packInfos.Count == 0)
		{
			WriteUnsignedVarInt(0);
			return;
		}
		WriteUnsignedVarInt((uint) packInfos.Count); // LE
		foreach (PackIdVersion info in packInfos)
		{
			Write(info.Id);
			Write(info.Version);
			Write(info.SubPackName);
		}
	}

	public ResourcePackIdVersions ReadResourcePackIdVersions()
	{
		uint count = ReadUnsignedVarInt();

		var packInfos = new ResourcePackIdVersions();
		for (int i = 0; i < count; i++)
		{
			string id = ReadString();
			string version = ReadString();
			string subPackName = ReadString();
			var info = new PackIdVersion
			{
				Id = id,
				Version = version,
				SubPackName = subPackName
			};
			packInfos.Add(info);
		}

		return packInfos;
	}

	public void Write(ResourcePackIds ids)
	{
		if (ids == null)
		{
			Write((short) 0);
			return;
		}
		Write((short) ids.Count);

		foreach (string id in ids) Write(id);
	}

	public ResourcePackIds ReadResourcePackIds()
	{
		int count = ReadShort();

		var ids = new ResourcePackIds();
		for (int i = 0; i < count; i++)
		{
			string id = ReadString();
			ids.Add(id);
		}

		return ids;
	}

	public void Write(Skin skin)
	{
		Write(skin.SkinId);
		Write(skin.PlayFabId);
		Write(skin.ResourcePatch);
		Write(skin.Width);
		Write(skin.Height);
		WriteByteArray(skin.Data);

		if (skin.Animations?.Count > 0)
		{
			Write(skin.Animations.Count);
			foreach (Animation animation in skin.Animations)
			{
				Write(animation.ImageWidth);
				Write(animation.ImageHeight);
				WriteByteArray(animation.Image);
				Write(animation.Type);
				Write(animation.FrameCount);
				Write(animation.Expression);
			}
		}
		else
			Write(0);

		Write(skin.Cape.ImageWidth);
		Write(skin.Cape.ImageHeight);
		WriteByteArray(skin.Cape.Data);
		Write(skin.GeometryData);
		Write(skin.GeometryDataVersion);
		Write(skin.AnimationData);

		Write(skin.Cape.Id);
		Write(skin.SkinId + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); // some unique skin id
		Write(skin.ArmSize);
		Write(skin.SkinColor);
		Write(skin.PersonaPieces.Count);
		foreach (PersonaPiece piece in skin.PersonaPieces)
		{
			Write(piece.PieceId);
			Write(piece.PieceType);
			Write(piece.PackId);
			Write(piece.IsDefaultPiece);
			Write(piece.ProductId);
		}
		Write(skin.SkinPieces.Count);
		foreach (SkinPiece skinPiece in skin.SkinPieces)
		{
			Write(skinPiece.PieceType);
			Write(skinPiece.Colors.Count);
			foreach (string color in skinPiece.Colors) Write(color);
		}

		Write(skin.IsPremiumSkin);
		Write(skin.IsPersonaSkin);
		Write(skin.Cape.OnClassicSkin);
		Write(skin.IsPrimaryUser);
		Write(skin.isOverride);
	}

	public Skin ReadSkin()
	{
		var skin = new Skin();

		skin.SkinId = ReadString();
		skin.PlayFabId = ReadString();
		skin.ResourcePatch = ReadString();
		skin.Width = ReadInt();
		skin.Height = ReadInt();
		skin.Data = ReadByteArray();

		int animationCount = ReadInt();
		for (int i = 0; i < animationCount; i++)
			skin.Animations.Add(
				new Animation
				{
					ImageWidth = ReadInt(),
					ImageHeight = ReadInt(),
					Image = ReadByteArray(),
					Type = ReadInt(),
					FrameCount = ReadFloat(),
					Expression = ReadInt()
				}
			);

		skin.Cape.ImageWidth = ReadInt();
		skin.Cape.ImageHeight = ReadInt();
		skin.Cape.Data = ReadByteArray();
		skin.GeometryData = ReadString();
		skin.GeometryDataVersion = ReadString();
		skin.AnimationData = ReadString();

		skin.Cape.Id = ReadString();
		ReadString(); // fullSkinId
		skin.ArmSize = ReadString();
		skin.SkinColor = ReadString();
		int personaPieceCount = ReadInt();
		for (int i = 0; i < personaPieceCount; i++)
		{
			var p = new PersonaPiece();
			p.PieceId = ReadString();
			p.PieceType = ReadString();
			p.PackId = ReadString();
			p.IsDefaultPiece = ReadBool();
			p.ProductId = ReadString();
			skin.PersonaPieces.Add(p);
		}

		int skinPieceCount = ReadInt();
		for (int i = 0; i < skinPieceCount; i++)
		{
			var piece = new SkinPiece();
			piece.PieceType = ReadString();
			int colorAmount = ReadInt();
			for (int i2 = 0; i2 < colorAmount; i2++) piece.Colors.Add(ReadString());
			skin.SkinPieces.Add(piece);
		}

		skin.IsPremiumSkin = ReadBool();
		skin.IsPersonaSkin = ReadBool();
		skin.Cape.OnClassicSkin = ReadBool();
		skin.IsPrimaryUser = ReadBool();
		skin.isOverride = ReadBool();
		//Log.Debug($"SkinId={skin.SkinId}");
		//Log.Debug($"SkinData lenght={skin.Data.Length}");
		//Log.Debug($"CapeData lenght={skin.Cape.Data.Length}");
		//Log.Debug("\n" + HexDump(skin.Cape.Data));
		//Log.Debug($"SkinGeometryName={skin.GeometryName}");
		//Log.Debug($"SkinGeometry lenght={skin.GeometryData.Length}");

		return skin;
	}

	public void Write(Recipes recipes)
	{
		WriteUnsignedVarInt((uint) recipes.Count);
		int UniqueId = 1;
		foreach (Recipe recipe in recipes)
		{
			switch (recipe)
			{
				case ShapelessRecipe shapelessRecipe:
				{
					WriteSignedVarInt(Shapeless); // Type

					ShapelessRecipe rec = shapelessRecipe;
					var uuid = new UUID(Guid.NewGuid().ToString());
					Write($"{uuid}");
					WriteVarInt(rec.Input.Count);
					foreach (Item stack in rec.Input) WriteRecipeIngredient(stack);
					WriteVarInt(rec.Result.Count);
					foreach (Item item in rec.Result)
					{
						item.RuntimeId = (int) BlockFactory.GetItemRuntimeId(item.Id, (byte) item.Metadata);
						Write(item, false);
					}
					Write(rec.Id);
					Write(rec.Block);
					WriteSignedVarInt(0); // priority
					Write((byte) 1); // recipe unlocking requirement 1 - always unlocked
					WriteVarInt(UniqueId); // unique id
					if (!RecipeManager.resultMapLocked) RecipeManager.resultMap.Add(UniqueId, rec.Result[0]);
					break;
				}
				case ShapedRecipe shapedRecipe:
				{
					WriteSignedVarInt(Shaped); // Type

					ShapedRecipe rec = shapedRecipe;
					var uuid = new UUID(Guid.NewGuid().ToString());
					Write($"{uuid}");
					WriteSignedVarInt(rec.Width);
					WriteSignedVarInt(rec.Height);
					for (int w = 0; w < rec.Width; w++)
					for (int h = 0; h < rec.Height; h++)
						WriteRecipeIngredient(rec.Input[(h * rec.Width) + w]);
					WriteVarInt(rec.Result.Count);
					foreach (Item item in rec.Result)
					{
						item.RuntimeId = (int) BlockFactory.GetItemRuntimeId(item.Id, (byte) item.Metadata);
						Write(item, false);
					}
					Write(rec.Id);
					Write(rec.Block);
					WriteUnsignedVarInt(0); // priority
					Write(true); // symmetric
					Write((byte) 1); // recipe unlocking requirement 1 - always unlocked
					WriteVarInt(UniqueId); // unique id
					if (!RecipeManager.resultMapLocked) RecipeManager.resultMap.Add(UniqueId, rec.Result[0]);
					break;
				}
				case SmeltingRecipe smeltingRecipe:
				{
					SmeltingRecipe rec = smeltingRecipe;
					if (rec.Input.Metadata == 0)
					{
						WriteSignedVarInt(Furnace);
						WriteSignedVarInt(rec.Input.Id);
						Write(rec.Result, false);
						Write(rec.Block);
					}
					else
					{
						WriteSignedVarInt(FurnaceData);
						WriteSignedVarInt(rec.Input.Id);
						WriteSignedVarInt(rec.Input.Metadata);
						Write(rec.Result, false);
						Write(rec.Block);
					}
					break;
				}
				case MultiRecipe multiRecipe:
				{
					WriteSignedVarInt(Multi); // Type
					Write(recipe.Id);
					WriteVarInt(UniqueId); // unique id
					break;
				}
			}
			UniqueId++;
		}
		RecipeManager.resultMapLocked = true;
	}

	public Recipes ReadRecipes()
	{
		var recipes = new Recipes();

		int count = (int) ReadUnsignedVarInt();
		Log.Warn($"[McpeCraftingData] Received {count} recipes");

		for (int i = 0; i < count; i++)
		{
			int recipeType = ReadSignedVarInt();

			//Log.Trace($"Read recipe no={i} type={recipeType}");

			if (recipeType < 0 /*|| len == 0*/)
			{
				Log.Error("Read void recipe");
				break;
			}

			switch (recipeType)
			{
				case Shapeless:
				case ShulkerBox:
				{
					var recipe = new ShapelessRecipe();
					ReadString(); // some unique id
					int ingrediensCount = ReadVarInt(); // 
					for (int j = 0; j < ingrediensCount; j++) recipe.Input.Add(ReadRecipeData());
					int resultCount = ReadVarInt(); // 1?
					for (int j = 0; j < resultCount; j++) recipe.Result.Add(ReadItem(false));
					recipe.Id = ReadUUID(); // Id
					recipe.Block = ReadString(); // block?
					ReadSignedVarInt(); // priority
					byte unlockReq = ReadByte(); // unlock
					if (unlockReq == 0)
					{
						int ingredientCount = ReadVarInt();
						for (int a = 0; a < ingredientCount; a++) ReadRecipeData();
					}
					recipe.UniqueId = ReadVarInt(); // unique id
					//recipes.Add(recipe);
					//Log.Error("Read shapeless recipe");
					Log.Debug($"Shapeless: {recipe.Id} | {recipe.Block} | {ingrediensCount}  | {resultCount} | {recipe.UniqueId}");
					break;
				}
				case Shaped:
				{
					string uniqueid = ReadString(); // some unique id
					//Log.Debug($"shaped u id {uniqueid}");
					int width = ReadSignedVarInt(); // Width
					int height = ReadSignedVarInt(); // Height
					//Log.Debug($"1 {width} {height}");
					var recipe = new ShapedRecipe(width, height);
					if (width > 3 || height > 3)
						throw new Exception("Wrong number of ingredience. Width=" + width + ", height=" + height);
					for (int w = 0; w < width; w++)
					for (int h = 0; h < height; h++)
						recipe.Input[(h * width) + w] = ReadRecipeData();
					int resultCount = ReadVarInt(); // 1?
					//Log.Debug($"2 {resultCount}");
					for (int j = 0; j < resultCount; j++) recipe.Result.Add(ReadItem(false));
					recipe.Id = ReadUUID(); // Id
					//Log.Debug($"3 {recipe.Id}");
					recipe.Block = ReadString(); // block?
					ReadUnsignedVarInt(); // priority
					bool symetric = ReadBool(); // symetric
					byte unlockReq = ReadByte(); // unlock
					if (unlockReq == 0)
					{
						int ingredientCount = ReadVarInt();
						for (int a = 0; a < ingredientCount; a++) ReadRecipeData();
					}
					recipe.UniqueId = ReadVarInt(); // unique id
					recipes.Add(recipe);
					Log.Debug($"Shaped: {recipe.Id} | {recipe.Block} | {width} | {height} | {resultCount} | {recipe.UniqueId} | {symetric} | {unlockReq}");
					break;
				}
				case Furnace:
				{
					var recipe = new SmeltingRecipe();
					short id = (short) ReadSignedVarInt(); // input (with metadata)
					//Log.Debug($"item id{id}");
					Item result = ReadItem(false); // Result
					recipe.Block = ReadString(); // block?
					recipe.Input = ItemFactory.GetItem(id);
					recipe.Result = result;
					//recipes.Add(recipe);
					//Log.Error("Read furnace recipe");
					Log.Debug($"Furnace Input={id}, meta={""} Item={result.Id}, Meta={result.Metadata}");
					break;
				}
				case FurnaceData:
				{
					//const ENTRY_FURNACE_DATA = 3;
					var recipe = new SmeltingRecipe();
					short id = (short) ReadSignedVarInt(); // input (with metadata) 
					short meta = (short) ReadSignedVarInt(); // input (with metadata) 
					Item result = ReadItem(false); // Result
					recipe.Block = ReadString(); // block?
					recipe.Input = ItemFactory.GetItem(id, meta);
					recipe.Result = result;
					//recipes.Add(recipe);
					//Log.Error("Read smelting recipe");
					Log.Debug($"Smelting Input={id}, meta={meta} Item={result.Id}, Meta={result.Metadata}");
					break;
				}
				case Multi:
				{
					var recipe = new MultiRecipe();
					recipe.Id = ReadUUID();
					recipe.UniqueId = ReadVarInt(); // unique id
					//recipes.Add(recipe);
					break;
				}
				case ShapelessChemistry:
				{
					var recipe = new ShapelessRecipe();
					ReadString(); // some unique id
					int ingrediensCount = ReadVarInt(); // 
					for (int j = 0; j < ingrediensCount; j++) recipe.Input.Add(ReadRecipeData());
					int resultCount = ReadVarInt(); // 1?
					for (int j = 0; j < resultCount; j++) recipe.Result.Add(ReadItem(false));
					recipe.Id = ReadUUID(); // Id
					recipe.Block = ReadString(); // block?
					ReadSignedVarInt(); // priority
					recipe.UniqueId = ReadVarInt(); // unique id
					//recipes.Add(recipe);
					//Log.Error("Read shapeless recipe");
					break;
				}
				case ShapedChemistry:
				{
					ReadString(); // some unique id
					int width = ReadSignedVarInt(); // Width
					int height = ReadSignedVarInt(); // Height
					var recipe = new ShapedRecipe(width, height);
					if (width > 3 || height > 3)
						throw new Exception("Wrong number of ingredience. Width=" + width + ", height=" + height);
					for (int w = 0; w < width; w++)
					for (int h = 0; h < height; h++)
						recipe.Input[(h * width) + w] = ReadRecipeData();

					int resultCount = ReadVarInt(); // 1?
					for (int j = 0; j < resultCount; j++) recipe.Result.Add(ReadItem(false));
					recipe.Id = ReadUUID(); // Id
					recipe.Block = ReadString(); // block?
					ReadSignedVarInt(); // priority
					recipe.UniqueId = ReadVarInt(); // unique id
					//recipes.Add(recipe);
					break;
				}
				case SmithingTrim:
				{
					var recipe = new SmithingTrimRecipe();
					recipe.RecipeId = ReadString();
					recipe.Template = ReadRecipeData();
					recipe.Input = ReadRecipeData();
					recipe.Addition = ReadRecipeData();
					recipe.Block = ReadString();
					recipe.UniqueId = ReadVarInt();
					//recipes.Add(recipe);
					Log.Debug($"SmithingTrimRecipe: {recipe.RecipeId} | {recipe.Template} | {recipe.Input} | {recipe.Addition} | {recipe.Block} | {recipe.UniqueId}");
					break;
				}
				case SmithingTransform:
				{
					var recipe = new SmithingTransformRecipe();
					recipe.RecipeId = ReadString(); // some unique id
					recipe.Template = ReadRecipeData();
					recipe.Input = ReadRecipeData();
					recipe.Addition = ReadRecipeData();
					recipe.Output = ReadItem(false);
					recipe.Block = ReadString(); // block?
					recipe.UniqueId = ReadVarInt(); // unique id
					//recipes.Add(recipe);
					Log.Debug($"SmithingTransformRecipe: {recipe.RecipeId} | {recipe.Template} | {recipe.Input} | {recipe.Addition} | {recipe.Block} | {recipe.UniqueId}");
					break;
				}
				default:
					Log.Error($"Read unknown recipe type: {recipeType}");
					//ReadBytes(len);
					break;
			}
		}

		Log.Warn($"[McpeCraftingData] Done reading {count} recipes\n");

		return recipes;
	}

	public void WriteRecipeIngredient(Item stack)
	{
		if (stack == null || stack.Id == 0)
		{
			Write(false);
			WriteVarInt(0);
			return;
		}
		Write(true);
		TranslatedItem translated = ItemFactory.Translator.ToNetworkId(stack.Id, stack.Metadata);
		if (translated.Id != stack.Id)
		{
			Write((short) translated.Id); // item is item
			Write(translated.Meta);
		}
		else
		{
			Write(stack.Id); // item is block
			Write(stack.Metadata);
		}
		WriteSignedVarInt(stack.Count);
	}

	public Item ReadRecipeData()
	{
		short type = ReadByte();
		//Log.Debug($"recipe type {type}");
		if (type == 1)
		{
			short id = ReadShort();
			short meta = ReadShort();
			short count = (short) ReadSignedVarInt();
			//Log.Debug($"Used desc data {id} ; {meta} ; {count}");
			return ItemFactory.GetItem(id, meta, count);
		}
		if (type == 2)
		{
			string expression = ReadString();
			int version = ReadByte();
			short count = (short) ReadSignedVarInt();
			//Log.Debug($"Used desc data {expression} ; {version} {count}");
			return ItemFactory.GetItem(ItemFactory.GetItemIdByName(expression));
		}
		if (type == 3)
		{
			string sId = ReadString();
			short count = (short) ReadSignedVarInt();
			//Log.Debug($"Used desc data {sId} ; {count}");
			return ItemFactory.GetItem(sId, 0, count);
		}
		if (type == 4)
		{
			string sId = ReadString();
			short meta = ReadShort();
			//Log.Debug($"Used desc data {sId} ; {meta}");
			return new ItemAir();
		}
		if (type == 5)
		{
			string stri = ReadString();
			//Log.Debug($"Used desc data {stri} {count}");
			ItemFactory.GetItem(ItemFactory.GetItemIdByName(stri));
		}
		short coun = (short) ReadSignedVarInt();
		//Log.Debug($"Used desc data 0 ; {coun}");
		return new ItemAir();
	}

	public Item ReadShapedRecipeIngredient()
	{
		short type = ReadByte();
		if (type == -1)
		{
		}
		//Log.Debug($"Used desc type {type}");
		return new ItemAir();
	}


	public void Write(PotionContainerChangeRecipe[] recipes)
	{
		WriteSignedVarInt(0);
	}

	public PotionContainerChangeRecipe[] ReadPotionContainerChangeRecipes()
	{
		int count = (int) ReadUnsignedVarInt();
		var recipes = new PotionContainerChangeRecipe[count];
		for (int i = 0; i < recipes.Length; i++)
		{
			var recipe = new PotionContainerChangeRecipe();
			recipe.Input = ReadVarInt();
			recipe.Ingredient = ReadVarInt();
			recipe.Output = ReadVarInt();

			recipes[i] = recipe;
		}

		return recipes;
	}

	public void Write(MaterialReducerRecipe[] reducerRecipes)
	{
		WriteUnsignedVarInt((uint) reducerRecipes.Length);

		for (int i = 0; i < reducerRecipes.Length; i++)
		{
			MaterialReducerRecipe recipe = reducerRecipes[i];
			WriteVarInt((recipe.Input << 16) | recipe.InputMeta);
			WriteUnsignedVarInt((uint) recipe.Output.Length);

			foreach (MaterialReducerRecipe.MaterialReducerRecipeOutput output in recipe.Output)
			{
				WriteVarInt(output.ItemId);
				WriteVarInt(output.ItemCount);
			}
		}
	}

	public MaterialReducerRecipe[] ReadMaterialReducerRecipes()
	{
		int count = (int) ReadUnsignedVarInt();
		var recipes = new MaterialReducerRecipe[count];
		for (int i = 0; i < recipes.Length; i++)
		{
			int inputIdAndMeta = ReadVarInt();
			int inputId = inputIdAndMeta >> 16;
			int inputMeta = inputIdAndMeta & 0x7fff;

			int outputCount = (int) ReadUnsignedVarInt();
			var outputs = new MaterialReducerRecipe.MaterialReducerRecipeOutput[outputCount];

			for (int o = 0; o < outputs.Length; o++)
			{
				int itemId = ReadVarInt();
				int itemCount = ReadVarInt();

				outputs[o] = new MaterialReducerRecipe.MaterialReducerRecipeOutput(itemId, itemCount);
			}

			var recipe = new MaterialReducerRecipe(inputId, inputMeta, outputs);

			recipes[i] = recipe;
		}

		return recipes;
	}

	public void Write(PotionTypeRecipe[] recipes)
	{
		WriteSignedVarInt(0);
	}

	public PotionTypeRecipe[] ReadPotionTypeRecipes()
	{
		int count = (int) ReadUnsignedVarInt();
		var recipes = new PotionTypeRecipe[count];
		for (int i = 0; i < recipes.Length; i++)
		{
			var recipe = new PotionTypeRecipe();
			recipe.Input = ReadVarInt();
			recipe.InputMeta = ReadVarInt();
			recipe.Ingredient = ReadVarInt();
			recipe.IngredientMeta = ReadVarInt();
			recipe.Output = ReadVarInt();
			recipe.OutputMeta = ReadVarInt();

			recipes[i] = recipe;
		}

		return recipes;
	}

	public void Write(MapInfo map)
	{
		WriteSignedVarLong(map.MapId);
		WriteUnsignedVarInt(map.UpdateType);
		Write((byte) 0); // dimension
		Write(false); // Locked
		Write(map.Origin);

		if ((map.UpdateType & MapUpdateFlagInitialisation) != 0)
		{
			WriteUnsignedVarInt(1);
			WriteSignedVarLong(map.MapId);
		}

		if ((map.UpdateType & (MapUpdateFlagInitialisation | MapUpdateFlagDecoration | MapUpdateFlagTexture)) != 0) Write((byte) map.Scale);

		if ((map.UpdateType & MapUpdateFlagDecoration) != 0)
		{
			int countTrackedObj = map.TrackedObjects.Length;

			WriteUnsignedVarInt((uint) countTrackedObj);
			foreach (MapTrackedObject trackedObject in map.TrackedObjects)
				if (trackedObject is EntityMapTrackedObject entity)
				{
					Write(0);
					WriteSignedVarLong(entity.EntityId);
				}
				else if (trackedObject is BlockMapTrackedObject block)
				{
					Write(1);
					Write(block.Coordinates);
				}

			int count = map.Decorators.Length;

			WriteUnsignedVarInt((uint) count);
			foreach (MapDecorator decorator in map.Decorators)
				if (decorator is EntityMapDecorator entity)
					WriteSignedVarLong(entity.EntityId);
				else if (decorator is BlockMapDecorator block) Write(block.Coordinates);

			WriteUnsignedVarInt((uint) count);
			foreach (MapDecorator decorator in map.Decorators)
			{
				Write(decorator.Icon);
				Write(decorator.Rotation);
				Write(decorator.X);
				Write(decorator.Z);
				Write(decorator.Label);
				WriteUnsignedVarInt(decorator.Color);
			}
		}

		if ((map.UpdateType & MapUpdateFlagTexture) != 0)
		{
			WriteSignedVarInt(map.Col);
			WriteSignedVarInt(map.Row);

			WriteSignedVarInt(map.XOffset);
			WriteSignedVarInt(map.ZOffset);

			WriteUnsignedVarInt((uint) (map.Col * map.Row));
			int i = 0;
			for (int col = 0; col < map.Col; col++)
			for (int row = 0; row < map.Row; row++)
			{
				byte r = map.Data[i++];
				byte g = map.Data[i++];
				byte b = map.Data[i++];
				byte a = map.Data[i++];
				uint color = BitConverter.ToUInt32(new byte[] { r, g, b, 0xff }, 0);
				WriteUnsignedVarInt(color);
			}
		}
	}

	public MapInfo ReadMapInfo()
	{
		var map = new MapInfo();

		map.MapId = ReadSignedVarLong();
		map.UpdateType = (byte) ReadUnsignedVarInt();
		ReadByte(); // Dimension (waste)
		ReadBool(); // Locked (waste)

		if ((map.UpdateType & MapUpdateFlagInitialisation) == MapUpdateFlagInitialisation)
		{
			// Entities
			uint count = ReadUnsignedVarInt();
			for (int i = 0; i < count - 1; i++) // This is some weird shit vanilla is doing with counting.
			{
				long eid = ReadSignedVarLong();
			}
		}

		if ((map.UpdateType & MapUpdateFlagTexture) == MapUpdateFlagTexture || (map.UpdateType & MapUpdateFlagDecoration) == MapUpdateFlagDecoration) map.Scale = ReadByte();
		//Log.Warn($"Reading scale {map.Scale}");
		if ((map.UpdateType & MapUpdateFlagDecoration) == MapUpdateFlagDecoration)
			// Decorations
			//Log.Warn("Got decoration update, reading it");
			try
			{
				uint entityCount = ReadUnsignedVarInt();
				for (int i = 0; i < entityCount; i++)
				{
					int type = ReadInt();
					if (type == 0)
					{
						// entity
						long q = ReadSignedVarLong();
					}
					else if (type == 1)
					{
						// block
						BlockCoordinates b = ReadBlockCoordinates();
					}
				}

				uint count = ReadUnsignedVarInt();
				map.Decorators = new MapDecorator[count];
				for (int i = 0; i < count; i++)
				{
					var decorator = new MapDecorator();
					decorator.Icon = ReadByte();
					decorator.Rotation = ReadByte();
					decorator.X = ReadByte();
					decorator.Z = ReadByte();
					decorator.Label = ReadString();
					decorator.Color = ReadUnsignedVarInt();
					map.Decorators[i] = decorator;
				}
			}
			catch (Exception e)
			{
				Log.Error($"Error while reading decorations for map={map}", e);
			}

		if ((map.UpdateType & MapUpdateFlagTexture) == MapUpdateFlagTexture)
			// Full map
			try
			{
				map.Col = ReadSignedVarInt();
				map.Row = ReadSignedVarInt(); //

				map.XOffset = ReadSignedVarInt(); //
				map.ZOffset = ReadSignedVarInt(); //
				ReadUnsignedVarInt(); // size
				for (int col = 0; col < map.Col; col++)
				for (int row = 0; row < map.Row; row++)
					ReadUnsignedVarInt();
			}
			catch (Exception e)
			{
				Log.Error($"Errror while reading map data for map={map}", e);
			}

		//else
		//{
		//	Log.Warn($"Unknown map-type 0x{map.UpdateType:X2}");
		//}

		//map.MapId = ReadLong();
		//var readBytes = ReadBytes(3);
		////Log.Warn($"{HexDump(readBytes)}");
		//map.UpdateType = ReadByte(); //
		//var bytes = ReadBytes(6);
		////Log.Warn($"{HexDump(bytes)}");

		//map.Direction = ReadByte(); //
		//map.X = ReadByte(); //
		//map.Z = ReadByte(); //

		//if (map.UpdateType == 0x06)
		//{
		//	// Full map
		//	try
		//	{
		//		if (bytes[4] == 1)
		//		{
		//			map.Col = ReadInt();
		//			map.Row = ReadInt(); //

		//			map.XOffset = ReadInt(); //
		//			map.ZOffset = ReadInt(); //

		//			map.Data = ReadBytes(map.Col*map.Row*4);
		//		}
		//	}
		//	catch (Exception e)
		//	{
		//		Log.Error($"Errror while reading map data for map={map}", e);
		//	}
		//}
		//else if (map.UpdateType == 0x04)
		//{
		//	// Map update
		//}
		//else
		//{
		//	Log.Warn($"Unknown map-type 0x{map.UpdateType:X2}");
		//}

		return map;
	}

	public pixelList ReadPixelList()
	{
		var mapData = new pixelList();

		int listSize = ReadInt();
		for (int i = 0; i < listSize; i++)
			mapData.mapData.Add(new pixelsData
			{
				pixel = ReadUnsignedVarInt(),
				index = ReadShort()
			});
		return mapData;
	}

	public void Write(ScoreEntries list)
	{
		if (list == null) list = new ScoreEntries();

		Write((byte) (list.FirstOrDefault() is ScoreEntryRemove ? McpeSetScore.Types.Remove : McpeSetScore.Types.Change));
		WriteUnsignedVarInt((uint) list.Count);
		foreach (ScoreEntry entry in list)
		{
			WriteSignedVarLong(entry.Id);
			Write(entry.ObjectiveName);
			Write(entry.Score);

			if (entry is ScoreEntryRemove) continue;

			if (entry is ScoreEntryChangePlayer player)
			{
				Write((byte) McpeSetScore.ChangeTypes.Player);
				WriteSignedVarLong(player.EntityId);
			}
			else if (entry is ScoreEntryChangeEntity entity)
			{
				Write((byte) McpeSetScore.ChangeTypes.Entity);
				WriteSignedVarLong(entity.EntityId);
			}
			else if (entry is ScoreEntryChangeFakePlayer fakePlayer)
			{
				Write((byte) McpeSetScore.ChangeTypes.FakePlayer);
				Write(fakePlayer.CustomName);
			}
		}
	}

	public ScoreEntries ReadScoreEntries()
	{
		var list = new ScoreEntries();
		byte type = ReadByte();
		uint length = ReadUnsignedVarInt();
		for (int i = 0; i < length; ++i)
		{
			long entryId = ReadSignedVarLong();
			string entryObjectiveName = ReadString();
			uint entryScore = ReadUint();

			ScoreEntry entry = null;

			if (type == (int) McpeSetScore.Types.Remove)
				entry = new ScoreEntryRemove();
			else
			{
				var changeType = (McpeSetScore.ChangeTypes) ReadByte();
				switch (changeType)
				{
					case McpeSetScore.ChangeTypes.Player:
						entry = new ScoreEntryChangePlayer { EntityId = ReadSignedVarLong() };
						break;
					case McpeSetScore.ChangeTypes.Entity:
						entry = new ScoreEntryChangeEntity { EntityId = ReadSignedVarLong() };
						break;
					case McpeSetScore.ChangeTypes.FakePlayer:
						entry = new ScoreEntryChangeFakePlayer { CustomName = ReadString() };
						break;
				}
			}

			if (entry == null) continue;

			entry.Id = entryId;
			entry.ObjectiveName = entryObjectiveName;
			entry.Score = entryScore;

			list.Add(entry);
		}

		return list;
	}

	public void Write(ScoreboardIdentityEntries list)
	{
		if (list == null) list = new ScoreboardIdentityEntries();

		Write((byte) (list.FirstOrDefault() is ScoreboardClearIdentityEntry ? McpeSetScoreboardIdentity.Operations.ClearIdentity : McpeSetScoreboardIdentity.Operations.RegisterIdentity));
		WriteUnsignedVarInt((uint) list.Count);
		foreach (ScoreboardIdentityEntry entry in list)
		{
			WriteSignedVarLong(entry.Id);
			if (entry is ScoreboardRegisterIdentityEntry reg) WriteSignedVarLong(reg.EntityId);
		}
	}

	public ScoreboardIdentityEntries ReadScoreboardIdentityEntries()
	{
		var list = new ScoreboardIdentityEntries();

		var type = (McpeSetScoreboardIdentity.Operations) ReadByte();
		uint length = ReadUnsignedVarInt();
		for (int i = 0; i < length; ++i)
		{
			long scoreboardId = ReadSignedVarLong();

			switch (type)
			{
				case McpeSetScoreboardIdentity.Operations.RegisterIdentity:
					list.Add(new ScoreboardRegisterIdentityEntry
					{
						Id = scoreboardId,
						EntityId = ReadSignedVarLong()
					});
					break;
				case McpeSetScoreboardIdentity.Operations.ClearIdentity:
					list.Add(new ScoreboardClearIdentityEntry { Id = scoreboardId });
					break;
			}

			// https://github.com/pmmp/PocketMine-MP/commit/39808dd94f4f2d1716eca31cb5a1cfe9000b6c38#diff-041914be0a0493190a4911ae5c4ac502R62
		}

		return list;
	}

	public Experiments ReadExperiments()
	{
		var experiments = new Experiments();
		int count = ReadInt();

		for (int i = 0; i < count; i++)
		{
			string experimentName = ReadString();
			bool enabled = ReadBool();
			experiments.Add(new Experiments.Experiment(experimentName, enabled));
		}
		return experiments;
	}

	public void Write(Experiments experiments)
	{
		if (experiments == null)
		{
			Write(0);
			return;
		}
		Write(experiments.Count);

		foreach (Experiments.Experiment experiment in experiments)
		{
			Write(experiment.Name);
			Write(experiment.Enabled);
		}
	}

	public void Write(EducationUriResource resource)
	{
		Write(resource.ButtonName);
		Write(resource.LinkUri);
	}

	public EducationUriResource ReadEducationUriResource()
	{
		string name = ReadString();
		string uri = ReadString();

		return new EducationUriResource(name, uri);
	}

	public void Write(UpdateSubChunkBlocksPacketEntry entry)
	{
		Write(entry.Coordinates);
		WriteUnsignedVarInt(entry.BlockRuntimeId);
		WriteUnsignedVarInt(entry.Flags);
		WriteUnsignedVarLong(entry.SyncedUpdatedEntityUniqueId);
		WriteUnsignedVarInt(entry.SyncedUpdateType);
	}

	public UpdateSubChunkBlocksPacketEntry ReadUpdateSubChunkBlocksPacketEntry()
	{
		var entry = new UpdateSubChunkBlocksPacketEntry();
		entry.Coordinates = ReadBlockCoordinates();
		entry.BlockRuntimeId = ReadUnsignedVarInt();
		entry.Flags = ReadUnsignedVarInt();
		entry.SyncedUpdatedEntityUniqueId = ReadUnsignedVarLong();
		entry.SyncedUpdateType = ReadUnsignedVarInt();

		return entry;
	}

	public void Write(UpdateSubChunkBlocksPacketEntry[] entries)
	{
		WriteUnsignedVarInt((uint) entries.Length);
		foreach (UpdateSubChunkBlocksPacketEntry entry in entries)
			Write(entry);
	}

	public UpdateSubChunkBlocksPacketEntry[] ReadUpdateSubChunkBlocksPacketEntrys()
	{
		uint count = ReadUnsignedVarInt();
		var entries = new UpdateSubChunkBlocksPacketEntry[(int) count];

		for (int i = 0; i < entries.Length; i++) entries[i] = ReadUpdateSubChunkBlocksPacketEntry();

		return entries;
	}

	public void Write(HeightMapData data)
	{
		if (data == null)
		{
			Write((byte) SubChunkPacketHeightMapType.NoData);

			return;
		}

		if (data.IsAllTooHigh)
		{
			Write((byte) SubChunkPacketHeightMapType.AllTooHigh);

			return;
		}

		if (data.IsAllTooLow)
		{
			Write((byte) SubChunkPacketHeightMapType.AllTooLow);

			return;
		}

		Write((byte) SubChunkPacketHeightMapType.Data);

		for (int i = 0; i < data.Heights.Length; i++) Write((byte) data.Heights[i]);
	}

	public HeightMapData ReadHeightMapData()
	{
		var type = (SubChunkPacketHeightMapType) ReadByte();

		if (type != SubChunkPacketHeightMapType.Data)
			return null;

		short[] heights = new short[256];

		for (int i = 0; i < heights.Length; i++) heights[i] = ReadByte();

		return new HeightMapData(heights);
	}

	public void Write(SubChunkPositionOffset offset)
	{
		Write(offset.XOffset);
		Write(offset.YOffset);
		Write(offset.ZOffset);
	}

	public SubChunkPositionOffset ReadSubChunkPositionOffset()
	{
		var result = new SubChunkPositionOffset();
		result.XOffset = ReadSByte();
		result.YOffset = ReadSByte();
		result.ZOffset = ReadSByte();
		return result;
	}

	public void Write(SubChunkPositionOffset[] offsets)
	{
		Write(offsets.Length);

		foreach (SubChunkPositionOffset offset in offsets) Write(offset);
	}

	public SubChunkPositionOffset[] ReadSubChunkPositionOffsets()
	{
		int count = ReadInt();
		var offsets = new SubChunkPositionOffset[count];

		for (int i = 0; i < offsets.Length; i++) offsets[i] = ReadSubChunkPositionOffset();

		return offsets;
	}

	public DimensionData ReadDimensionData()
	{
		var data = new DimensionData();
		data.MaxHeight = ReadVarInt();
		data.MinHeight = ReadVarInt();
		data.Generator = ReadVarInt();

		return data;
	}

	public void Write(DimensionData data)
	{
		WriteVarInt(data.MaxHeight);
		WriteVarInt(data.MinHeight);
		WriteVarInt(data.Generator);
	}

	public void Write(DimensionDefinitions definitions)
	{
		WriteUnsignedVarInt((uint) definitions.Count);

		foreach (KeyValuePair<string, DimensionData> def in definitions)
		{
			Write(def.Key);
			Write(def.Value);
		}
	}

	public DimensionDefinitions ReadDimensionDefinitions()
	{
		var definitions = new DimensionDefinitions();

		uint count = ReadUnsignedVarInt();
		for (int i = 0; i < count; i++)
		{
			string stringId = ReadString();
			DimensionData data = ReadDimensionData();

			definitions.TryAdd(stringId, data);
		}

		return definitions;
	}

	public void Write(PropertySyncData syncData)
	{
		if (syncData == null)
		{
			WriteUnsignedVarInt(0);
			WriteUnsignedVarInt(0);
			return;
		}
		WriteUnsignedVarInt((uint) syncData.intProperties.Count);

		foreach (KeyValuePair<uint, int> intP in syncData.intProperties)
		{
			WriteUnsignedVarInt(intP.Key);
			WriteSignedVarInt(intP.Value);
		}

		WriteUnsignedVarInt((uint) syncData.floatProperties.Count);

		foreach (KeyValuePair<uint, float> intF in syncData.floatProperties)
		{
			WriteUnsignedVarInt(intF.Key);
			Write(intF.Value);
		}
	}

	public PropertySyncData ReadPropertySyncData()
	{
		var syncData = new PropertySyncData();
		uint countInt = ReadUnsignedVarInt();
		for (int i = 0; i < countInt; i++) syncData.intProperties.Add(ReadUnsignedVarInt(), ReadVarInt());

		uint countFloat = ReadUnsignedVarInt();
		for (int i = 0; i < countFloat; i++) syncData.floatProperties.Add(ReadUnsignedVarInt(), ReadFloat());
		return syncData;
	}

	public EmoteIds ReadEmoteId()
	{
		var Ids = new EmoteIds();
		uint emoteCount = ReadUnsignedVarInt();
		for (int i = 0; i < (int) emoteCount; i++) Ids.emoteId.Add(ReadUUID());
		return Ids;
	}

	public void Write(EmoteIds Ids)
	{
		Write(Ids.emoteId.Count);
		foreach (UUID emoteIds in Ids.emoteId) Write(emoteIds);
	}

	public PlayerBlockActions ReadPlayerBlockActions()
	{
		var actions = new PlayerBlockActions();
		int actionCount = ReadSignedVarInt();
		for (int i = 0; i < actionCount; i++)
		{
			var actionType = (PlayerAction) ReadSignedVarInt();
			if (actionType is PlayerAction.StartBreak or PlayerAction.AbortBreak or PlayerAction.StopBreak or PlayerAction.Breaking or PlayerAction.PredictDestroyBlock or PlayerAction.ContinueDestroyBlock)
				actions.PlayerBlockAction.Add(new PlayerBlockActionData
				{
					PlayerActionType = actionType,
					BlockCoordinates = new BlockCoordinates(ReadSignedVarInt(), ReadSignedVarInt(), ReadSignedVarInt()),
					Facing = ReadVarInt()
				});
			else
				actions.PlayerBlockAction.Add(new PlayerBlockActionData { PlayerActionType = actionType });
		}
		return actions;
	}

	public fogStack Read()
	{
		var stack = new fogStack();
		uint effectCount = ReadUnsignedVarInt();
		for (int i = 0; i < (int) effectCount; i++) stack.fogList.Add(ReadString());
		return stack;
	}

	public void Write(fogStack stack)
	{
		WriteUnsignedVarInt((uint) stack.fogList.Count);
		foreach (string effect in stack.fogList) Write(effect);
	}

	public bool CanRead()
	{
		return _reader.Position < _reader.Length;
	}

	public void SetEncodedMessage(byte[] encodedMessage)
	{
		_encodedMessage = encodedMessage;
	}

	public virtual void Reset()
	{
		ResetPacket();

		ReliabilityHeader = new ReliabilityHeader();

		NoBatch = false;
		ForceClear = false;

		_encodedMessage = null;
		Bytes = null;
		Timer.Restart();

		_writer?.Close();
		_reader?.Close();
		_buffer?.Close();
		_writer = null;
		_reader = null;
		_buffer = null;
	}

	protected virtual void ResetPacket()
	{
	}

	public virtual byte[] Encode()
	{
		byte[] cache = _encodedMessage;
		if (cache != null) return cache;

		lock (_encodeSync)
		{
			// This construct to avoid unnecessary contention and double encoding.
			if (_encodedMessage != null) return _encodedMessage;

			// Dynamic pooling. If this packet has been registered as a large object in previous
			// runs, we use the pooled stream for it instead to avoid LOB allocations
			bool isLob = _isLob.ContainsKey(Id);
			_buffer = isLob ? _streamManager.GetStream() : new MemoryStream();
			using (_writer = new BinaryWriter(_buffer, Encoding.UTF8, true))
			{
				EncodePacket();

				_writer.Flush();
				// This WILL allocate LOB. Need to convert this to work with array segment and pool it.
				// then we will use GetBuffer instead.
				// Also remember to move dispose entirely to Reset (dispose) when that happens.
				var buffer = (MemoryStream) _buffer;
				_encodedMessage = buffer.ToArray();
				if (!isLob && _encodedMessage.Length >= 85_000) _isLob.TryAdd(Id, true);
				//Log.Warn($"LOB {GetType().Name} {_encodedMessage.Length}, IsLOB={_isLob}");
			}
			_buffer.Dispose();

			_writer = null;
			_buffer = null;

			return _encodedMessage;
		}
	}

	protected virtual void EncodePacket()
	{
		_buffer.Position = 0;
		if (IsMcpe) WriteVarInt(Id);
		else Write((byte) Id);
	}

	[Obsolete("Use decode with ReadOnlyMemory<byte> instead.")]
	public virtual Packet Decode(byte[] buffer)
	{
		return Decode(new ReadOnlyMemory<byte>(buffer));
	}

	public virtual Packet Decode(ReadOnlyMemory<byte> buffer)
	{
		Bytes = buffer;
		_reader = new MemoryStreamReader(buffer);

		DecodePacket();

		if (Log.IsDebugEnabled && _reader.Position != buffer.Length) Log.Warn($"{GetType().Name}: Still have {buffer.Length - _reader.Position} bytes to read!!\n{HexDump(buffer.ToArray())}");

		_reader.Dispose();
		_reader = null;

		return this;
	}

	protected virtual void DecodePacket()
	{
		Id = IsMcpe ? ReadVarInt() : ReadByte();
	}

	public abstract void PutPool();

	public static string HexDump(ReadOnlyMemory<byte> bytes, int bytesPerLine = 16, bool printLineCount = false)
	{
		return HexDump(bytes.Span, bytesPerLine, printLineCount);
	}

	private static string HexDump(ReadOnlySpan<byte> bytes, in int bytesPerLine, in bool printLineCount)
	{
		var sb = new StringBuilder();
		for (int line = 0; line < bytes.Length; line += bytesPerLine)
		{
			byte[] lineBytes = bytes.Slice(line).ToArray().Take(bytesPerLine).ToArray();
			if (printLineCount) sb.AppendFormat("{0:x8} ", line);
			sb.Append(string.Join(" ", lineBytes.Select(b => b.ToString("x2"))
					.ToArray())
				.PadRight(bytesPerLine * 3));
			sb.Append(" ");
			sb.Append(new string(lineBytes.Select(b => b < 32 ? '.' : (char) b)
				.ToArray()));
			sb.AppendLine();
		}
		return sb.ToString();
	}

	public static string ToJson(Packet message)
	{
		var jsonSerializerSettings = new JsonSerializerSettings
		{
			PreserveReferencesHandling = PreserveReferencesHandling.Arrays,
			Formatting = Formatting.Indented
		};
		jsonSerializerSettings.Converters.Add(new NbtIntConverter());
		jsonSerializerSettings.Converters.Add(new NbtStringConverter());
		jsonSerializerSettings.Converters.Add(new IPAddressConverter());
		jsonSerializerSettings.Converters.Add(new IPEndPointConverter());

		return JsonConvert.SerializeObject(message, jsonSerializerSettings);
	}
}

/// Base package class
public abstract class Packet<T> : Packet, IDisposable where T : Packet<T>, new()
{
	private static readonly ILog Log = LogManager.GetLogger(typeof(Packet<T>));

	private static readonly ObjectPool<T> Pool = new(() => new T());

	private bool _isPermanent;
	private long _referenceCounter;

	[JsonIgnore] public bool IsPooled { get; private set; }

	[JsonIgnore]
	public long ReferenceCounter
	{
		get => _referenceCounter;
		set => _referenceCounter = value;
	}

	public void Dispose()
	{
		PutPool();
	}


	public T MarkPermanent(bool permanent = true)
	{
		if (!IsPooled) throw new Exception("Tried to make non pooled item permanent");
		_isPermanent = permanent;

		return (T) this;
	}

	public T AddReferences(long numberOfReferences)
	{
		if (_isPermanent) return (T) this;

		if (!IsPooled) throw new Exception("Tried to reference count a non pooled item");
		Interlocked.Add(ref _referenceCounter, numberOfReferences);

		return (T) this;
	}

	public T AddReference(Packet<T> item)
	{
		if (_isPermanent) return (T) this;

		if (!item.IsPooled) throw new Exception("Item template needs to come from a pool");

		Interlocked.Increment(ref item._referenceCounter);
		return (T) item;
	}

	public T MakePoolable(long numberOfReferences = 1)
	{
		IsPooled = true;
		_referenceCounter = numberOfReferences;
		return (T) this;
	}


	public static T CreateObject(long numberOfReferences = 1)
	{
		T item = Pool.GetObject();
		item.IsPooled = true;
		item._referenceCounter = numberOfReferences;
		item.Timer.Restart();
		return item;
	}

	// DO NOT UNCOMMENT THIS!!!
	// It will have > 100% performance impact overall.
	//~Packet()
	//{
	//	if (_isPooled)
	//	{
	//		//Log.Error($"Unexpected dispose 0x{Id:x2} {GetType().Name}, IsPooled={_isPooled}, IsPermanent={_isPermanent}, Refs={_referenceCounter}");
	//	}
	//}

	public override void PutPool()
	{
		if (_isPermanent) return;
		if (!IsPooled) return;

		long counter = Interlocked.Decrement(ref _referenceCounter);
		if (counter > 0) return;

		if (counter < 0)
		{
			Log.Error($"Pooling error. Added pooled object too many times. 0x{Id:x2} {GetType().Name}, IsPooled={IsPooled}, IsPooled={_isPermanent}, Refs={_referenceCounter}");
			return;
		}

		Reset();

		IsPooled = false;

		//Pool.PutObject((T) this);
	}
}