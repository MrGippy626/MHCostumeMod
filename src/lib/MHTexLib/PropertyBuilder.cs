using System.IO;

namespace MHTexLib;

public class PropertyBuilder
{
	private readonly UpkFile _upk;

	private readonly MemoryStream _ms = new MemoryStream();

	private readonly BinaryWriter _w;

	public PropertyBuilder(UpkFile upk)
	{
		_upk = upk;
		_w = new BinaryWriter(_ms);
	}

	public byte[] ToArray()
	{
		_w.Flush();
		return _ms.ToArray();
	}

	private void WriteFName(string name, int number = 0)
	{
		_w.Write(_upk.RequireName(name));
		_w.Write(number);
	}

	public void IntProp(string name, int value)
	{
		WriteFName(name);
		WriteFName("IntProperty");
		_w.Write(4);
		_w.Write(0);
		_w.Write(value);
	}

	public void FloatProp(string name, float value, int arrayIndex = 0)
	{
		WriteFName(name);
		WriteFName("FloatProperty");
		_w.Write(4);
		_w.Write(arrayIndex);
		_w.Write(value);
	}

	public void BoolProp(string name, bool value)
	{
		WriteFName(name);
		WriteFName("BoolProperty");
		_w.Write(0);
		_w.Write(0);
		_w.Write(value ? ((byte)1) : ((byte)0));
	}

	public void ByteProp(string name, string enumType, string valueName)
	{
		WriteFName(name);
		WriteFName("ByteProperty");
		_w.Write(8);
		_w.Write(0);
		WriteFName(enumType);
		WriteFName(valueName);
	}

	public void NameProp(string name, string valueName, int valueNum = 0)
	{
		WriteFName(name);
		WriteFName("NameProperty");
		_w.Write(8);
		_w.Write(0);
		WriteFName(valueName, valueNum);
	}

	public void ObjectProp(string name, int objRef)
	{
		WriteFName(name);
		WriteFName("ObjectProperty");
		_w.Write(4);
		_w.Write(0);
		_w.Write(objRef);
	}

	public void StructProp(string name, string structName, byte[] body)
	{
		WriteFName(name);
		WriteFName("StructProperty");
		_w.Write(body.Length);
		_w.Write(0);
		WriteFName(structName);
		_w.Write(body);
	}

	public void ArrayProp(string name, byte[] elementsPayload)
	{
		WriteFName(name);
		WriteFName("ArrayProperty");
		_w.Write(elementsPayload.Length);
		_w.Write(0);
		_w.Write(elementsPayload);
	}

	public void None()
	{
		WriteFName("None");
	}
}
