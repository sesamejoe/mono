using Mono;
using System.Configuration.Assemblies;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Reflection
{
	[StructLayout (LayoutKind.Sequential)]	
	partial class AssemblyName
	{
        public AssemblyName (string assemblyName)
        {
            if (assemblyName == null)
                throw new ArgumentNullException (nameof (assemblyName));
            if (assemblyName.Length == 0 || assemblyName [0] == '\0')
                throw new ArgumentException (SR.Format_StringZeroLength);

			using (var name = RuntimeMarshal.MarshalString (assemblyName)) {
				// TODO: Should use CoreRT AssemblyNameParser
				if (!ParseAssemblyName (name.Value, out var nativeName, out var isVersionDefined, out var isTokenDefined))
					throw new FileLoadException ("The assembly name is invalid.");

				try {
					unsafe {
						FillName (&nativeName, null, isVersionDefined, false, isTokenDefined);
					}
				} finally {
					RuntimeMarshal.FreeAssemblyName (ref nativeName, false);
				}
			}
        }

		unsafe byte [] ComputePublicKeyToken ()
		{
			var token = new byte [8];
			fixed (byte* pkt = _publicKeyToken)
			fixed (byte *pk = _publicKey)
				get_public_token (pkt, pk, _publicKey.Length);
			return token;
		}

		internal static AssemblyName Create (Assembly assembly, bool fillCodebase)
		{
			AssemblyName aname = new AssemblyName ();
			unsafe {
				MonoAssemblyName *native = GetNativeName (assembly.MonoAssembly);
				aname.FillName (native, fillCodebase ? assembly.CodeBase : null, true, true, true);
			}
			return aname;
		}		

		internal unsafe void FillName (MonoAssemblyName *native, string codeBase, bool addVersion, bool addPublickey, bool defaultToken)
		{
			_name = RuntimeMarshal.PtrToUtf8String (native->name);

			_flags = (AssemblyNameFlags) native->flags;

			_hashAlgorithm = (AssemblyHashAlgorithm) native->hash_alg;

			_versionCompatibility = AssemblyVersionCompatibility.SameMachine;

			if (addVersion) {
				var build = native->build == 65535 ? -1 : native->build;
				var revision = native->revision == 65535 ? -1 : native->revision;

				if (build == -1)
					_version = new Version (native->major, native->minor);
				else if (revision == -1)
					_version = new Version (native->major, native->minor, build);
				else
					_version = new Version (native->major, native->minor, build, revision);
			}

			_codeBase = codeBase;

			if (native->culture != IntPtr.Zero)
				_cultureInfo = CultureInfo.GetCultureInfo (RuntimeMarshal.PtrToUtf8String (native->culture));

			if (native->public_key != IntPtr.Zero) {
				_publicKey = RuntimeMarshal.DecodeBlobArray (native->public_key);
				_flags |= AssemblyNameFlags.PublicKey;
			} else if (addPublickey) {
				_publicKey = EmptyArray<byte>.Value;
				_flags |= AssemblyNameFlags.PublicKey;
			}

			// MonoAssemblyName keeps the public key token as an hexadecimal string
			if (native->public_key_token [0] != 0) {
				var keyToken = new byte [8];
				for (int i = 0, j = 0; i < 8; ++i) {
					keyToken [i] = (byte) (RuntimeMarshal.AsciHexDigitValue (native->public_key_token [j++]) << 4);
					keyToken [i] |= (byte) RuntimeMarshal.AsciHexDigitValue (native->public_key_token [j++]);
				}
				_publicKeyToken = keyToken;
			} else if (defaultToken) {
				_publicKeyToken = EmptyArray<byte>.Value;
			}
		}

		static AssemblyName GetFileInformationCore (string assemblyFile) 
		{
			unsafe {
				Assembly.InternalGetAssemblyName (Path.GetFullPath (assemblyFile), out var nativeName, out var codebase);

				var aname = new AssemblyName ();
				try {
					aname.FillName (&nativeName, codebase, true, false, true);
					return aname;
				} finally {
					RuntimeMarshal.FreeAssemblyName (ref nativeName, false);
				}
			}
		}		

		[MethodImplAttribute (MethodImplOptions.InternalCall)]
		static extern unsafe void get_public_token (byte* token, byte* pubkey, int len);

		[MethodImplAttribute (MethodImplOptions.InternalCall)]
		static extern unsafe MonoAssemblyName* GetNativeName (IntPtr assemblyPtr);

		[MethodImpl (MethodImplOptions.InternalCall)]
		static extern bool ParseAssemblyName (IntPtr name, out MonoAssemblyName aname, out bool is_version_definited, out bool is_token_defined);				
	}
}