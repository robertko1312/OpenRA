#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.FileFormats;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	[Serializable]
	public class SheetOverflowException : Exception
	{
		public SheetOverflowException(string message)
			: base(message) { }
	}

	// The enum values indicate the number of channels used by the type
	// They are not arbitrary IDs!
	public enum SheetType
	{
		Indexed = 1,
		BGRA = 4,
	}

	public sealed class SheetBuilder : IDisposable
	{
		public readonly SheetType Type;
		readonly List<Sheet> sheets = new List<Sheet>();
		readonly Func<Sheet> allocateSheet;

		Sheet current;
		TextureChannel channel;
		int rowHeight = 0;
		int2 p;

		public static Sheet AllocateSheet(SheetType type, int sheetSize)
		{
			return new Sheet(type, new Size(sheetSize, sheetSize));
		}

		public SheetBuilder(SheetType t)
			: this(t, Game.Settings.Graphics.SheetSize) { }

		public SheetBuilder(SheetType t, int sheetSize)
			: this(t, () => AllocateSheet(t, sheetSize)) { }

		public SheetBuilder(SheetType t, Func<Sheet> allocateSheet)
		{
			channel = t == SheetType.Indexed ? TextureChannel.Red : TextureChannel.RGBA;
			Type = t;
			current = allocateSheet();
			sheets.Add(current);
			this.allocateSheet = allocateSheet;
		}

		public Sprite Add(ISpriteFrame frame) { return Add(frame.Data, frame.Size, 0, frame.Offset); }
		public Sprite Add(byte[] src, Size size) { return Add(src, size, 0, float3.Zero); }
		public Sprite Add(byte[] src, Size size, float zRamp, float3 spriteOffset)
		{
			// Don't bother allocating empty sprites
			if (size.Width == 0 || size.Height == 0)
				return new Sprite(current, Rectangle.Empty, 0, spriteOffset, channel, BlendMode.Alpha);

			var rect = Allocate(size, zRamp, spriteOffset);
			Util.FastCopyIntoChannel(rect, src);
			current.CommitBufferedData();
			return rect;
		}

		public Sprite Add(Png src)
		{
			var rect = Allocate(new Size(src.Width, src.Height));
			Util.FastCopyIntoSprite(rect, src);
			current.CommitBufferedData();
			return rect;
		}

		public Sprite Add(Size size, byte paletteIndex)
		{
			var data = new byte[size.Width * size.Height];
			for (var i = 0; i < data.Length; i++)
				data[i] = paletteIndex;

			return Add(data, size);
		}

		TextureChannel? NextChannel(TextureChannel t)
		{
			var nextChannel = (int)t + (int)Type;
			if (nextChannel > (int)TextureChannel.Alpha)
				return null;

			return (TextureChannel)nextChannel;
		}

		public Sprite Allocate(Size imageSize) { return Allocate(imageSize, 0, float3.Zero); }
		public Sprite Allocate(Size imageSize, float zRamp, float3 spriteOffset)
		{
			if (imageSize.Width + p.X > current.Size.Width)
			{
				p = new int2(0, p.Y + rowHeight);
				rowHeight = imageSize.Height;
			}

			if (imageSize.Height > rowHeight)
				rowHeight = imageSize.Height;

			if (p.Y + imageSize.Height > current.Size.Height)
			{
				var next = NextChannel(channel);
				if (next == null)
				{
					current.ReleaseBuffer();
					current = allocateSheet();
					sheets.Add(current);
					channel = Type == SheetType.Indexed ? TextureChannel.Red : TextureChannel.RGBA;
				}
				else
					channel = next.Value;

				rowHeight = imageSize.Height;
				p = int2.Zero;
			}

			var rect = new Sprite(current, new Rectangle(p.X, p.Y, imageSize.Width, imageSize.Height), zRamp, spriteOffset, channel, BlendMode.Alpha);
			p += new int2(imageSize.Width, 0);

			return rect;
		}

		public Sheet Current { get { return current; } }
		public TextureChannel CurrentChannel { get { return channel; } }
		public IEnumerable<Sheet> AllSheets { get { return sheets; } }

		public void Dispose()
		{
			foreach (var sheet in sheets)
				sheet.Dispose();
			sheets.Clear();
		}
	}
}
