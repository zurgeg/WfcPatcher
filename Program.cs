﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WfcPatcher {
	class Program {
		static void Main( string[] args ) {
			foreach ( string s in args ) {
				try {
					PatchFile( s );
				} catch ( Exception ex ) {
					Console.WriteLine( "Failed patching " + s );
					Console.WriteLine( ex.ToString() );
					Console.WriteLine();
				}
			}
		}

		static void PatchFile( string filename ) {
			Console.WriteLine( "Reading & Copying ROM..." );
			var ndsSrc = new System.IO.FileStream( filename, System.IO.FileMode.Open );
			var nds = new System.IO.FileStream( filename + ".wfc.nds", System.IO.FileMode.Create );
			Util.CopyStream( ndsSrc, nds, (int)ndsSrc.Length );
			ndsSrc.Close();

			// http://dsibrew.org/wiki/DSi_Cartridge_Header

			// overlays
			Console.WriteLine( "Patching Overlays..." );
			nds.Position = 0x50;
			uint arm9overlayoff = nds.ReadUInt32();
			uint arm9overlaylen = nds.ReadUInt32();
			uint arm7overlayoff = nds.ReadUInt32();
			uint arm7overlaylen = nds.ReadUInt32();

			PatchOverlay( nds, arm9overlayoff, arm9overlaylen );
			PatchOverlay( nds, arm7overlayoff, arm7overlaylen );

			nds.Close();
		}

		static void PatchOverlay( System.IO.FileStream nds, uint pos, uint len ) {
			// http://sourceforge.net/p/devkitpro/ndstool/ci/master/tree/source/ndsextract.cpp
			// http://sourceforge.net/p/devkitpro/ndstool/ci/master/tree/source/overlay.h
			// header compression info from http://gbatemp.net/threads/recompressing-an-overlay-file.329576/

			nds.Position = 0x048;
			uint fatOffset = nds.ReadUInt32();

			for ( uint i = 0; i < len; i += 0x20 ) {
				nds.Position = pos + i;
				uint id = nds.ReadUInt32();
				uint ramAddr = nds.ReadUInt32();
				uint ramSize = nds.ReadUInt32();
				uint bssSize = nds.ReadUInt32();
				uint sinitInit = nds.ReadUInt32();
				uint sinitInitEnd = nds.ReadUInt32();
				uint fileId = nds.ReadUInt32();
				uint compressedSize = nds.ReadUInt24();
				byte compressedBitmask = (byte)nds.ReadByte();

				nds.Position = fatOffset + 8 * id;
				uint overlayPositionStart = nds.ReadUInt32();
				uint overlayPositionEnd = nds.ReadUInt32();
				uint overlaySize = overlayPositionEnd - overlayPositionStart;

				nds.Position = overlayPositionStart;
				byte[] data = new byte[overlaySize];
				nds.Read( data, 0, (int)overlaySize );

				blz blz = new blz();
				byte[] decData;

				bool compressed = ( compressedBitmask & 0x01 ) == 0x01;
				if ( compressed ) {
					decData = blz.BLZ_Decode( data );
				} else {
					decData = data;
				}


				if ( ReplaceInData( decData ) ) {
					// if something was replaced, put it back into the ROM
					if ( compressed ) {

						uint newCompressedSize = 0;
						data = blz.BLZ_Encode( decData, 0 );
						newCompressedSize = (uint)data.Length;

						byte[] newCompressedSizeBytes = BitConverter.GetBytes( newCompressedSize );
						nds.Position = pos + i + 0x1C;
						nds.Write( newCompressedSizeBytes, 0, 3 );

					} else {
						data = decData;
					}

					nds.Position = overlayPositionStart;
					nds.Write( data, 0, data.Length );

					overlayPositionEnd = (uint)nds.Position;

					// padding
					int newOverlaySize = data.Length;
					int diff = (int)overlaySize - newOverlaySize;
					for ( int j = 0; j < diff; ++j ) {
						nds.WriteByte( 0xFF );
					}

					// new file end offset
					byte[] newPosEndData = BitConverter.GetBytes( overlayPositionEnd );
					nds.Position = fatOffset + 8 * id + 4;
					nds.Write( newPosEndData, 0, 4 );
				}

			}
		}

		static bool ReplaceInData( byte[] data ) {
			string search = "https://";
			string replace = "http://";
			byte[] searchBytes = Encoding.ASCII.GetBytes( search );
			byte[] replaceBytes = Encoding.ASCII.GetBytes( replace );
			int requiredPadding = searchBytes.Length - replaceBytes.Length;

			var results = data.Locate( searchBytes );
			if ( results.Length == 0 ) {
				return false;
			}

			foreach ( int result in results ) {
				string originalString = Util.GetTextAscii( data, result );
				if ( originalString == "https://" ) { continue; } // don't replace lone https, probably used for strcmp to figure out if an URL is SSL or not
				string replacedString = originalString.Replace( search, replace );
				byte[] replacedStringBytes = Encoding.ASCII.GetBytes( replacedString );

				int i = 0;
				for ( ; i < replacedStringBytes.Length; ++i ) {
					data[result + i] = replacedStringBytes[i];
				}
				for ( ; i < replacedStringBytes.Length + requiredPadding; ++i ) {
					data[result + i] = 0x00;
				}
			}

			return true;
		}
	}
}
