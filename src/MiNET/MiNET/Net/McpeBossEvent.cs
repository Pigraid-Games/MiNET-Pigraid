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

namespace MiNET.Net;

public partial class McpeBossEvent
{
	public uint color = 0xff00ff00;
	public float healthPercent;
	public uint overlay = 0xff00ff00;
	public long playerId;
	public string title;
	public ushort unknown6;

	partial void AfterEncode()
	{
		switch ((Type) eventType)
		{
			case Type.AddPlayer:
			case Type.RemovePlayer:
				WriteSignedVarLong(playerId);
				break;

			case Type.UpdateProgress:
				Write(healthPercent);
				break;

			case Type.UpdateName:
				Write(title);
				break;

			case Type.AddBoss:
				Write(title);
				Write(healthPercent);
				goto case Type.UpdateOptions;
			case Type.UpdateOptions:
				Write(unknown6);
				goto case Type.UpdateStyle;
			case Type.UpdateStyle:
				WriteUnsignedVarInt(color);
				WriteUnsignedVarInt(overlay);
				break;
			case Type.Query:
				WriteEntityId(playerId);
				break;
		}
	}

	public override void Reset()
	{
		base.Reset();
	}

	partial void AfterDecode()
	{
		switch ((Type) eventType)
		{
			case Type.AddPlayer:
			case Type.RemovePlayer:
				// Entity Unique ID
				playerId = ReadSignedVarLong();
				break;
			case Type.UpdateProgress:
				// float
				healthPercent = ReadFloat();
				break;
			case Type.UpdateName:
				// string
				title = ReadString();
				break;
			case Type.AddBoss:
				// string
				title = ReadString();
				// float
				healthPercent = ReadFloat();
				goto case Type.UpdateOptions;
			case Type.UpdateOptions:
				// ushort?
				unknown6 = ReadUshort();
				goto case Type.UpdateStyle;
			case Type.UpdateStyle:
				// NOOP
				color = ReadUnsignedVarInt();
				overlay = ReadUnsignedVarInt();
				break;
			case Type.Query:
				playerId = ReadSignedVarLong();
				break;
		}
	}
}