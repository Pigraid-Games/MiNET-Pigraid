﻿﻿#region LICENSE

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
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using PigNet.Plugins;
using Plugins_Version = PigNet.Plugins.Version;
using Version = PigNet.Plugins.Version;

namespace PigNet.Net.Packets.Mcpe;

public class EnumData
{
	public string Name { get; set; }
	public string[] Values { get; set; }
	public EnumData(string name, string[] values)
	{
		Name = name;
		Values = values;
	}
}

public class McpeAvailableCommands : Packet<McpeAvailableCommands>
{
	private static readonly ILog Log = LogManager.GetLogger(typeof(McpeAvailableCommands));

	public CommandSet CommandSet { get; set; }

	public McpeAvailableCommands()
	{
		Id = 0x4c;
		IsMcpe = true;
	}

	protected override void DecodePacket()
	{
		base.DecodePacket();

		CommandSet = new CommandSet();
		var stringValues = new List<string>();
		{
			uint count = ReadUnsignedVarInt();
			Log.Debug($"String values {count}");
			for (int i = 0; i < count; i++)
			{
				string str = ReadString();
				Log.Debug($"{i} - {str}");
				stringValues.Add(str);
			}
		}

		var chainedSubCommandValueNames = new List<string>();
		{
			// Chained sub command value names?
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Chained sub command value names {count}");
			for (int i = 0; i < count; i++)
			{
				var value = ReadString();
				chainedSubCommandValueNames.Add(value);
				Log.Debug($"\t{value}");
			}
		}

		int stringValuesCount = stringValues.Count();
		{
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Postfix values {count}");
			for (int i = 0; i < count; i++)
			{
				string s = ReadString();
				Log.Debug(s);
			}
		}

		EnumData[] enums;
		{
			uint count = ReadUnsignedVarInt();
			enums = new EnumData[count];
			Log.Debug($"Enum indexes {count}");

			for (int i = 0; i < count; i++)
			{
				string enumName = ReadString();
				uint enumValueCount = ReadUnsignedVarInt();
				string[] enumValues = new string[enumValueCount];

				Log.Debug($"{i} - {enumName}:{enumValueCount}");
				for (int j = 0; j < enumValueCount; j++)
				{
					int idx;
					if (stringValuesCount <= byte.MaxValue)
					{
						idx = ReadByte();
					}
					else if (stringValuesCount <= short.MaxValue)
					{
						idx = ReadShort();
					}
					else
					{
						idx = ReadInt();
					}

					enumValues[j] = stringValues[idx];
					Log.Debug($"{enumName}, {idx} - {stringValues[idx]}");
				}

				enums[i] = new EnumData(enumName, enumValues);
			}
		}

		var allChainedSubCommandData = new Dictionary<string, Dictionary<string, short>>();
		{
			// Chained sub command data?
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Soft enums {count}");
			for (int i = 0; i < count; i++)
			{
				var values = new Dictionary<string, short>();

				string name = ReadString();
				Log.Debug($"chained sub command name {name}");
				uint valCount = ReadUnsignedVarInt();
				for (int j = 0; j < valCount; j++)
				{
					var valueName = chainedSubCommandValueNames[ReadShort()];
					var valueType = ReadShort();
					Log.Debug($"\t{name} valueName:{valueName} valueType:{valueType}");

					values.Add(valueName, valueType);
				}

				allChainedSubCommandData.Add(name, values);
			}
		}

		{
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Commands definitions {count}");
			for (int i = 0; i < count; i++)
			{
				Command command = new Command();
				command.Versions = new Plugins_Version[1];
				string commandName = ReadString();
				string description = ReadString();
				int flags = ReadShort();
				int permissions = ReadByte();

				command.Name = commandName;

				Plugins_Version version = new Plugins_Version();
				version.Description = description;

				int aliasEnumIndex = ReadInt();

				{
					// Chained sub command data?
					uint c = ReadUnsignedVarInt();
					Log.Debug($"Command chained sub command data {c}");
					for (int j = 0; j < c; j++)
					{
						var chainedSubCommandData = allChainedSubCommandData.ElementAt(ReadShort());
						var valueType = ReadShort();
						//Log.Debug($"\tchainedSubCommandData: {chainedSubCommandData.Key}");
					}
				}

				uint overloadCount = ReadUnsignedVarInt();
				version.Overloads = new Dictionary<string, Overload>();
				for (int j = 0; j < overloadCount; j++)
				{
					var isChaining = ReadBool();

					Overload overload = new Overload();
					overload.Input = new Input();

					uint parameterCount = ReadUnsignedVarInt();
					overload.Input.Parameters = new Parameter[parameterCount];
					Log.Debug($"{commandName}, {description}, isChaining={isChaining}, flags={flags}, {((CommandPermission) permissions)}, alias={aliasEnumIndex}, overloads={overloadCount}, params={parameterCount}");
					for (int k = 0; k < parameterCount; k++)
					{
						string commandParamName = ReadString();
						var paramType = ReadInt();
						var optional = ReadBool();
						var paramFlags = ReadByte();

						Parameter parameter = new Parameter()
						{
							Name = commandParamName,
							Optional = optional,
							Type = (CommandParameterType) (paramType & 0xffff)
						};

						overload.Input.Parameters[k] = parameter;

						if ((paramType & (int) CommandParameterType.EnumFlag) != 0) //Enum
						{
							var paramEnum = enums[paramType & 0xffff];
							parameter.EnumValues = paramEnum.Values;
							parameter.EnumType = paramEnum.Name;
							parameter.Type = CommandParameterType.EnumFlag;
						}
						else if ((paramType & (int) CommandParameterType.PostfixFlag) != 0) //Postfix
						{
							var paramEnum = enums[paramType & 0xffff];
							parameter.EnumValues = paramEnum.Values;
							parameter.EnumType = paramEnum.Name;
							parameter.Type = CommandParameterType.EnumFlag;
						}

						//Log.Debug($"\t{commandParamName}, 0x{tmp:X4}, 0x{tmp1:X4}, {isEnum}, {isSoftEnum}, {(GetParameterTypeName(commandParamType))}, {commandParamEnumIndex}, {commandParamSoftEnumIndex}, {commandParamPostfixIndex}, {optional}, {unknown}");
					}

					version.Overloads.Add(j.ToString(), overload);
				}

				command.Versions[0] = version;
				CommandSet.Add(commandName, command);
			}
		}
		{
			// Soft enums?
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Soft enums {count}");
			for (int i = 0; i < count; i++)
			{
				string enumName = ReadString();
				Log.Debug($"Soft Enum {enumName}");
				uint valCount = ReadUnsignedVarInt();
				for (int j = 0; j < valCount; j++)
				{
					Log.Debug($"\t{enumName} value:{ReadString()}");
				}
			}
		}

		{
			// constraints
			uint count = ReadUnsignedVarInt();
			Log.Debug($"Constraints {count}");
			for (int i = 0; i < count; i++)
			{
				Log.Debug($"Constraint: {ReadInt()} _ {ReadInt()}");
				uint someCount = ReadUnsignedVarInt();
				for (int j = 0; j < someCount; j++)
				{
					Log.Debug($"\tUnknown byte: {ReadByte()}");
				}
			}
		}
	}

	protected override void EncodePacket()
	{
		base.EncodePacket();

		try
		{
			if (CommandSet == null || CommandSet.Count == 0)
			{
				Log.Warn("No commands to send");
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				WriteUnsignedVarInt(0);
				return;
			}

			CommandSet commands = CommandSet;

			List<string> stringList = [];
			{
				foreach (Command command in commands.Values)
				{
					string[] aliases = command.Versions[0].Aliases.Concat([command.Name]).ToArray();
					foreach (string alias in aliases)
					{
						if (!stringList.Contains(alias)) stringList.Add(alias);
					}

					Dictionary<string, Overload> overloads = command.Versions[0].Overloads;
					foreach (Overload overload in overloads.Values)
					{
						Parameter[] parameters = overload.Input.Parameters;
						if (parameters == null) continue;
						foreach (Parameter parameter in parameters)
						{
							if (parameter.Type != CommandParameterType.EnumFlag) continue;
							if (parameter.EnumValues == null) continue;
							foreach (string enumValue in parameter.EnumValues)
							{
								if (!stringList.Contains(enumValue)) stringList.Add(enumValue);
							}
						}
					}
				}

				WriteUnsignedVarInt((uint) stringList.Count); // Enum values
				foreach (string s in stringList)
				{
					Write(s);
					//Log.Debug($"String: {s}, {(short) stringList.IndexOf(s)} ");
				}
			}

			WriteUnsignedVarInt(0); // Chained sub command value names
			WriteUnsignedVarInt(0); // Postfixes

			List<string> enumList = [];
			foreach (Command command in commands.Values)
			{
				if (command.Versions[0].Aliases.Length > 0)
				{
					string aliasEnum = command.Name + "CommandAliases";
					if (!enumList.Contains(aliasEnum)) enumList.Add(aliasEnum);
				}

				Dictionary<string, Overload> overloads = command.Versions[0].Overloads;
				foreach (Overload overload in overloads.Values)
				{
					Parameter[] parameters = overload.Input.Parameters;
					if (parameters == null) continue;
					foreach (Parameter parameter in parameters)
					{
						if (parameter.Type != CommandParameterType.EnumFlag) continue;
						if (parameter.EnumValues == null) continue;

						if (!enumList.Contains(parameter.EnumType)) enumList.Add(parameter.EnumType);
					}
				}
			}

			//WriteUnsignedVarInt(0); // Enum indexes
			WriteUnsignedVarInt((uint) enumList.Count); // Enum indexes
			List<string> writtenEnumList = [];
			foreach (Command command in commands.Values)
			{
				if (command.Versions[0].Aliases.Length > 0)
				{
					string[] aliases = command.Versions[0].Aliases.Concat([command.Name]).ToArray();
					string aliasEnum = command.Name + "CommandAliases";
					if (!enumList.Contains(aliasEnum)) continue;
					if (writtenEnumList.Contains(aliasEnum)) continue;

					Write(aliasEnum);
					WriteUnsignedVarInt((uint) aliases.Length);
					foreach (string enumValue in aliases)
					{
						if (!stringList.Contains(enumValue)) Log.Error($"Expected enum value: {enumValue} in string list, but didn't find it.");
						switch (stringList.Count)
						{
							case <= byte.MaxValue:
								Write((byte) stringList.IndexOf(enumValue));
								break;
							case <= short.MaxValue:
								Write((short) stringList.IndexOf(enumValue));
								break;
							default:
								Write(stringList.IndexOf(enumValue));
								break;
						}

						//Log.Debug($"EnumType: {aliasEnum}, {enumValue}, {stringList.IndexOf(enumValue)} ");
					}
				}

				Dictionary<string, Overload> overloads = command.Versions[0].Overloads;
				foreach (Overload overload in overloads.Values)
				{
					Parameter[] parameters = overload.Input.Parameters;
					if (parameters == null) continue;
					foreach (Parameter parameter in parameters)
					{
						if (parameter.Type != CommandParameterType.EnumFlag) continue;
						if (parameter.EnumValues == null) continue;

						if (!enumList.Contains(parameter.EnumType)) continue;
						if (writtenEnumList.Contains(parameter.EnumType)) continue;

						writtenEnumList.Add(parameter.EnumType);

						Write(parameter.EnumType);
						WriteUnsignedVarInt((uint) parameter.EnumValues.Length);
						foreach (var enumValue in parameter.EnumValues)
						{
							if (!stringList.Contains(enumValue)) Log.Error($"Expected enum value: {enumValue} in string list, but didn't find it.");
							switch (stringList.Count)
							{
								case <= byte.MaxValue:
									Write((byte) stringList.IndexOf(enumValue));
									break;
								case <= short.MaxValue:
									Write((short) stringList.IndexOf(enumValue));
									break;
								default:
									Write(stringList.IndexOf(enumValue));
									break;
							}

							//Log.Debug($"EnumType: {parameter.EnumType}, {enumValue}, {stringList.IndexOf(enumValue)} ");
						}
					}
				}
			}

			WriteUnsignedVarInt(0); // Chained sub command data

			WriteUnsignedVarInt((uint) commands.Count);
			foreach (Command command in commands.Values)
			{
				Write(command.Name);
				Write(command.Versions[0].Description);
				Write((short) 0); // flags
				Write((byte) command.Versions[0].CommandPermission); // permissions

				if (command.Versions[0].Aliases.Length > 0)
				{
					string aliasEnum = command.Name + "CommandAliases";
					Write(enumList.IndexOf(aliasEnum));
				}
				else Write(-1); // Enum index

				WriteUnsignedVarInt(0); // Chained sub command data

				//Log.Warn($"Writing command {command.Name}");

				Dictionary<string, Overload> overloads = command.Versions[0].Overloads;
				WriteUnsignedVarInt((uint) overloads.Count); // Overloads
				foreach (Overload overload in overloads.Values)
				{
					Write(false); // isChaining
					//Log.Warn($"Writing command: {command.Name}");

					Parameter[] parameters = overload.Input.Parameters;
					if (parameters == null)
					{
						WriteUnsignedVarInt(0); // Parameter count
						continue;
					}

					WriteUnsignedVarInt((uint) parameters.Length); // Parameter count
					foreach (Parameter parameter in parameters)
					{
						//Log.Debug($"Writing command overload parameter {command.Name}, {parameter.Name}, {parameter.Type}");

						Write(parameter.Name); // parameter name
						switch (parameter.Type)
						{
							case CommandParameterType.EnumFlag when parameter.EnumValues != null:
								Write((short) enumList.IndexOf(parameter.EnumType));
								Write((short) 0x30);
								break;
							case CommandParameterType.SoftEnumFlag when parameter.EnumValues != null:
								Write((short) 0); // soft enum index below
								Write((short) 0x0410);
								break;
							default:
								Write((short) parameter.Type); // param type
								Write((short) 0x10);
								break;
						}

						Write(parameter.Optional); // optional
						Write((byte) 0); // unknown
					}
				}
			}

			WriteUnsignedVarInt(1); //TODO: soft enums
			Write("CmdSoftEnumValues");
			Write(false);

			WriteUnsignedVarInt(0); //TODO: constraints
		}
		catch (Exception e)
		{
			Log.Error("Sending commands", e);
			//throw;
		}
	}
}