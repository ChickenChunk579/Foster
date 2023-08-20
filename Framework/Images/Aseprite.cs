using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Foster.Framework;

public class Aseprite : Aseprite.IUserDataTarget
{
	private static int MulUn8(int a, int b)
	{
		var t = a * b + 0x80;
		return (t >> 8) + t >> 8;
	}

	public delegate Color BlendFn(Color backdrop, Color src, int opacity);

	static readonly BlendFn BlendNormal = (Color backdrop, Color src, int opacity) =>
	{
		int r, g, b, a;

		if (backdrop.A == 0)
		{
			r = src.R;
			g = src.G;
			b = src.B;
		}
		else if (src.A == 0)
		{
			r = backdrop.R;
			g = backdrop.G;
			b = backdrop.B;
		}
		else
		{
			r = backdrop.R + MulUn8(src.R - backdrop.R, opacity);
			g = backdrop.G + MulUn8(src.G - backdrop.G, opacity);
			b = backdrop.B + MulUn8(src.B - backdrop.B, opacity);
		}

		a = backdrop.A + MulUn8(Math.Max(0, src.A - backdrop.A), opacity);
		if (a == 0)
			r = g = b = 0;

		return new Color((byte)r, (byte)g, (byte)b, (byte)a);
	};

	public enum BlendMode
	{
		Normal = 0,
		Multiply = 1,
		Screen = 2,
		Overlay = 3,
		Darken = 4,
		Lighten = 5,
		ColorDodge = 6,
		ColorBurn = 7,
		HardLight = 8,
		SoftLight = 9,
		Difference = 10,
		Exclusion = 11,
		Hue = 12,
		Saturation = 13,
		Color = 14,
		Luminosity = 15,
		Addition = 16,
		Subtract = 17,
		Divide = 18,
	}

	public enum LayerType
	{
		Normal = 0,
		Group = 1,
		Tilemap = 2,
	}

	enum ChunkType
	{
		Unknown = -1,
		OldPalette = 0x0004,
		OldPalette2 = 0x0011,
		Layer = 0x2004,
		Cel = 0x2005,
		CelExtra = 0x2006,
		ColorProfile = 0x2007,
		ExternalFiles = 0x2008,
		Mask = 0x2016,
		Path = 0x2017,
		Tags = 0x2018,
		Palette = 0x2019,
		UserData = 0x2020,
		Slice = 0x2022,
		Tileset = 0x2023,
	}

	public enum LoopDir
	{
		Forward = 0,
		Reverse = 1,
		PingPong = 2,
		PingPongReverse = 3,
	}

	public struct UserData
	{
		public string? Text;
		public Color? Color;
	}

	interface IUserDataTarget
	{
		void SetUserData(UserData userData);
	}

	[Flags]
	public enum LayerFlags
	{
		None = 0,
		Visible = 1,
		Editable = 2,
		LockMovement = 4,
		Background = 8,
		PreferLinkedCels = 16,
		DisplayCollapsed = 32,
		Reference = 64,
	}

	public class Layer : IUserDataTarget
	{
		public LayerFlags Flags;
		public LayerType Type;
		public int ChildLevel;
		public Point2 DefaultSize;
		public BlendMode BlendMode;
		public byte Opacity;
		public string? Name;
		public int TilesetIndex;
		public UserData? UserData;

		public bool Visible => Flags.Has(LayerFlags.Visible);
		public bool Editable => Flags.Has(LayerFlags.Editable);
		public bool LockMovement => Flags.Has(LayerFlags.LockMovement);
		public bool Background => Flags.Has(LayerFlags.Background);
		public bool PreferLinkedCels => Flags.Has(LayerFlags.PreferLinkedCels);
		public bool DisplayCollapsed => Flags.Has(LayerFlags.DisplayCollapsed);
		public bool Reference => Flags.Has(LayerFlags.Reference);

		public void SetUserData(UserData userData) => UserData = userData;

		public string? UserDataText => UserData?.Text;
	}

	public class Frame : IUserDataTarget
	{
		public int Duration;
		public List<Cel> Cels = new();
		public UserData? UserData;

		public void SetUserData(UserData userData) => UserData = userData;
	}

	enum Format
	{
		Rgba = 32,
		Grayscale = 16,
		Indexed = 8,
	}

	enum CelType
	{
		RawImageData = 0,
		LinkedCel = 1,
		CompressedImage = 2,
		CompressedTilemap = 3
	}

	public class Cel : IUserDataTarget
	{
		public Layer Layer;
		public Point2 Pos;
		public byte Opacity;
		public int ZIndex;
		public Image? Image;
		public UserData? UserData;

		public Cel(Layer layer, Point2 pos, byte opacity, int zIndex)
		{
			Layer = layer;
			Pos = pos;
			Opacity = opacity;
			ZIndex = zIndex;
		}

		public void SetUserData(UserData userData) => UserData = userData;

		public string? UserDataText => UserData?.Text;
	}

	public class Tag
	{
		public int From;
		public int To;
		public LoopDir LoopDir;
		public int Repeat;
		public Color Color;
		public string? Name;
		public UserData? UserData;
	}

	public class Slice : IUserDataTarget
	{
		public string Name;
		public int[] Frames;
		public RectInt[] Bounds;
		public RectInt[]? NineSliceCenters;
		public Point2[]? Pivots;
		public UserData? UserData;

		public string? UserDataText => UserData?.Text;

		public int KeyCount => Bounds.Length;
		public int FirstFrame => Frames[0];
		public RectInt FirstBounds => Bounds[0];
		public RectInt? FirstNineSliceCenter => NineSliceCenters?[0];
		public Point2? FirstPivot => Pivots?[0];

		public Slice(string name, int count, bool nineSlices, bool pivots)
		{
			Name = name;
			Frames = new int[count];
			Bounds = new RectInt[count];
			if (nineSlices)
				NineSliceCenters = new RectInt[count];
			if (pivots)
				Pivots = new Point2[count];
		}

		public void SetUserData(UserData userData) => UserData = userData;
	}

	public Point2 Size;
	public Frame[] Frames = Array.Empty<Frame>();
	public Color[] Palette = Array.Empty<Color>();
	public Tag[] Tags = Array.Empty<Tag>();
	public List<Slice> Slices = new();
	public List<Layer> Layers = new();
	public UserData? SpriteUserData;

	public int Width => Size.X;
	public int Height => Size.Y;
	public string? UserDataText => SpriteUserData?.Text;

	public void SetUserData(UserData userData) => SpriteUserData = userData;

	public static Aseprite FromFile(string filePath)
	{
		using var file = File.OpenRead(filePath);
		using var bin = new BinaryReader(file);
		return new Aseprite(bin);
	}

	public Aseprite(BinaryReader bin)
	{
		byte ReadByte() => bin.ReadByte();
		ushort ReadWord() => bin.ReadUInt16();
		short ReadShort() => bin.ReadInt16();
		uint ReadDWord() => bin.ReadUInt32();
		int ReadLong() => bin.ReadInt32();
		string ReadString() => Encoding.UTF8.GetString(bin.ReadBytes(ReadWord()));
		void Skip(int count)
		{
			while (count-- > 0)
				bin.ReadByte();
		}

		// Parse the file header
		var fileSize = ReadDWord();
		if (ReadWord() != 0xA5E0)
			Log.Error("invalid aseprite file, magic number is wrong");
		Frames = new Frame[ReadWord()];
		Size.X = ReadWord();
		Size.Y = ReadWord();
		var format = (Format)ReadWord();
		ReadDWord(); // Flags (IGNORE)
		ReadWord(); // Speed (DEPRECATED)
		ReadDWord(); // Set be 0
		ReadDWord(); // Set be 0
		var transparentColorIndex = ReadByte();
		Skip(3);
		var colorCount = ReadWord();
		ReadByte(); // Pixel width
		ReadByte(); // Pixel height
		ReadShort(); // X position of the grid
		ReadShort(); // Y position of the grid
		ReadWord(); // Grid width
		ReadWord(); // Grid height
		Skip(84);

		byte[] buffer = new byte[Size.X * Size.Y * ((int)format / 8)];

		for (int f = 0; f < Frames.Length; ++f)
		{
			var frame = Frames[f] = new Frame();
			IUserDataTarget? userDataTarget = frame;
			int nextTagUserData = 0;

			// Parse the frame header
			ReadDWord(); // Bytes in this frame
			if (ReadWord() != 0xF1FA)
				Log.Error("frame magic number is incorrect");
			int oldChunkCount = ReadWord();
			frame.Duration = ReadWord();
			Skip(2); // For future (set to zero)
			int chunkCount = (int)ReadDWord();
			if (chunkCount == 0)
				chunkCount = oldChunkCount;

			// Parse all the frame chunks
			for (int ch = 0; ch < chunkCount; ++ch)
			{
				long chunkStart = bin.BaseStream.Position;
				var chunkSize = ReadDWord();
				var chunkType = (ChunkType)ReadWord();
				if (!Enum.IsDefined(chunkType))
					chunkType = ChunkType.Unknown;

				long chunkEnd = chunkStart + chunkSize;
				void SkipChunk() => bin.BaseStream.Position = chunkEnd;

				switch (chunkType)
				{
					case ChunkType.Palette:
						{
							int len = (int)ReadDWord();
							if (Palette == null || len > Palette.Length)
								Array.Resize(ref Palette, len);
							int first = (int)ReadDWord();
							int last = (int)ReadDWord();
							Skip(8);
							for (int i = first; i <= last; ++i)
							{
								var flags = ReadWord();
								Palette[i] = new Color(ReadByte(), ReadByte(), ReadByte(), ReadByte());
								if ((flags & 1) != 0)
									ReadString();
							}

							userDataTarget = this;
						}
						break;

					case ChunkType.Slice:
						{
							var count = (int)ReadDWord();
							var flags = ReadDWord();
							ReadDWord();
							var name = ReadString();

							var slice = new Slice(name, count, (flags & 1) != 0, (flags & 2) != 0);
							Slices.Add(slice);
							userDataTarget = slice;

							for (int i = 0; i < count; ++i)
							{
								slice.Frames[i] = (int)ReadDWord();
								slice.Bounds[i] = new RectInt(ReadLong(), ReadLong(), (int)ReadDWord(), (int)ReadDWord());
								if (slice.NineSliceCenters is RectInt[] centers)
									centers[i] = new RectInt(ReadLong(), ReadLong(), (int)ReadDWord(), (int)ReadDWord());
								if (slice.Pivots is Point2[] pivots)
									pivots[i] = new Point2(ReadLong(), ReadLong());
							}
						}
						break;

					case ChunkType.Tags:
						{
							Array.Resize(ref Tags, ReadWord());
							Skip(8);
							for (int t = 0; t < Tags.Length; ++t)
							{
								var tag = Tags[t] = new Tag();
								tag.From = ReadWord();
								tag.To = ReadWord();
								tag.LoopDir = (LoopDir)ReadByte();
								tag.Repeat = ReadWord();
								Skip(10);
								tag.Name = ReadString();
							}
							userDataTarget = null;
						}
						break;

					case ChunkType.UserData:
						{
							var userData = new UserData();
							var flags = ReadDWord();
							if ((flags & 1) != 0)
								userData.Text = ReadString();
							if ((flags & 2) != 0)
								userData.Color = new Color(ReadByte(), ReadByte(), ReadByte(), ReadByte());
							if ((flags & 4) != 0)
								SkipChunk();

							if (userDataTarget is IUserDataTarget target)
							{
								target.SetUserData(userData);
								userDataTarget = null;
							}
							else if (Tags is Tag[] tags && nextTagUserData < tags.Length)
								tags[nextTagUserData++].UserData = userData;
						}
						break;

					case ChunkType.Layer:
						{
							var layer = new Layer();
							Layers.Add(layer);
							userDataTarget = layer;

							layer.Flags = (LayerFlags)ReadWord();
							layer.Type = (LayerType)ReadWord();
							layer.ChildLevel = ReadWord();
							layer.DefaultSize = new Point2(ReadWord(), ReadWord());
							layer.BlendMode = (BlendMode)ReadWord();
							layer.Opacity = ReadByte();
							Skip(3);
							layer.Name = ReadString();
							if (layer.Type == LayerType.Tilemap)
								layer.TilesetIndex = (int)ReadDWord();
						}
						break;

					case ChunkType.Cel:
						{
							var layer = Layers[ReadWord()];
							var pos = new Point2(ReadShort(), ReadShort());
							var opacity = ReadByte();
							var type = (CelType)ReadWord();
							var zIndex = ReadShort();

							if (type == CelType.CompressedTilemap)
							{
								SkipChunk();
								continue;
							}

							var cel = new Cel(layer, pos, opacity, zIndex);
							frame.Cels.Add(cel);
							userDataTarget = cel;

							Skip(5);

							if (type == CelType.LinkedCel)
							{
								var linkedFrame = ReadWord();
								Cel linkedCel = Frames[linkedFrame].Cels.Find(c => c.Layer == layer)!;
								cel.Image = linkedCel.Image;
								continue;
							}

							var width = (int)ReadWord();
							var height = (int)ReadWord();
							var pixels = new Color[width * height];

							int decompressedLen = width * height * ((int)format / 8);
							if (buffer.Length < decompressedLen)
								Array.Resize(ref buffer, decompressedLen);

							if (type == CelType.RawImageData)
							{
								bin.Read(buffer, 0, decompressedLen);
							}
							else if (type == CelType.CompressedImage)
							{
								using var zip = new ZLibStream(bin.BaseStream, CompressionMode.Decompress, true);
								zip.ReadExactly(buffer, 0, decompressedLen);
							}

							switch (format)
							{
								case Format.Rgba:
									for (int i = 0, b = 0; i < pixels.Length; ++i, b += 4)
										pixels[i] = new Color(buffer[b], buffer[b + 1], buffer[b + 2], buffer[b + 3]);
									break;
								case Format.Grayscale:
									for (int i = 0, b = 0; i < pixels.Length; ++i, b += 4)
										pixels[i] = new Color(buffer[b], buffer[b], buffer[b], buffer[b + 1]);
									break;
								case Format.Indexed:
									for (int i = 0; i < pixels.Length; ++i)
										pixels[i] = Palette![buffer[i]];
									break;
							}

							cel.Image = new Image(width, height, pixels);

							SkipChunk();
						}
						break;

					default:
						SkipChunk();
						break;
				}
			}
		}
	}

	public Image?[]? RenderFrames(in RectInt slice, Predicate<Layer> layerFilter)
	{
		Image?[]? results = null;

		foreach (var layer in Layers)
		{
			if (!layerFilter(layer))
				continue;

			for (int i = 0; i < Frames.Length; ++i)
			{
				if (Frames[i].Cels.Find(cel => cel.Layer == layer) is not Cel cel)
					continue;
				if (cel.Image is not Image src)
					continue;
				if (!slice.Overlaps(new RectInt(cel.Pos.X, cel.Pos.Y, src.Width, src.Height)))
					continue;

				if (results is not Image?[] images)
					results = images = new Image?[Frames.Length];

				if (images[i] is not Image img)
					images[i] = img = new Image(slice.Width, slice.Height);

				// TODO: handle group layer opacity cascading
				int opacity = MulUn8(cel.Opacity, layer.Opacity);
				img.CopyPixels(src, cel.Pos - slice.TopLeft, (src, dst) => BlendNormal(dst, src, opacity));
			}
		}

		return results;
	}

	public Image? RenderFrame(int index, Predicate<Layer> layerFilter)
	{
		Image? image = null;

		foreach (var layer in Layers)
		{
			if (!layer.Visible || !layerFilter(layer))
				continue;
			if (Frames[index].Cels.Find(cel => cel.Layer == layer) is not Cel cel)
				continue;
			if (cel.Image is not Image src)
				continue;

			if (image is not Image dst)
				image = dst = new Image(Width, Height);

			int opacity = MulUn8(cel.Opacity, layer.Opacity);
			dst.CopyPixels(src, src.Bounds, cel.Pos, (src, dst) => BlendNormal(dst, src, opacity));
		}

		return image;
	}

	public Image?[]? RenderAllFrames(Predicate<Layer> layerFilter)
	{
		if (Frames.Length == 0)
			return null;
		return RenderFrames(0, Frames.Length - 1, layerFilter);
	}

	public Image?[]? RenderFrames(int from, int to, Predicate<Layer> layerFilter)
	{
		Image?[]? results = null;

		int len = (to - from) + 1;

		foreach (var layer in Layers)
		{
			if (!layer.Visible || !layerFilter(layer))
				continue;
			for (int i = 0; i < len; ++i)
			{
				if (Frames[from + i].Cels.Find(cel => cel.Layer == layer) is not Cel cel)
					continue;
				if (cel.Image is not Image src)
					continue;

				if (results is not Image?[] images)
					results = images = new Image?[len];

				if (images[i] is not Image dst)
					images[i] = dst = new Image(Size.X, Size.Y);

				int opacity = MulUn8(cel.Opacity, layer.Opacity);
				dst.CopyPixels(src, src.Bounds, cel.Pos, (src, dst) => BlendNormal(dst, src, opacity));
			}
		}

		return results;
	}
}
