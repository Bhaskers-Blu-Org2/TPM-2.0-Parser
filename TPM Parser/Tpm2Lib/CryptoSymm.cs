﻿/*++

Copyright (c) 2010-2015 Microsoft Corporation
Microsoft Confidential

*/
using System;

#if WINDOWS_UWP
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
#else // WINDOWS_UWP
#if !TSS_USE_BCRYPT
using System.Security.Cryptography;
#endif
#endif // !WINDOWS_UWP

namespace Tpm2Lib
{
    /// <summary>
    /// A helper class for doing symmetric cryptography based on 
    /// TPM structure definitions.
    /// </summary>
    public sealed class SymmCipher : IDisposable
    {
        public bool LimitedSupport = false;

#if TSS_USE_BCRYPT
        private BCryptKey Key;
        private byte[] KeyBuffer;
        private byte[] IV;

        private SymmCipher(BCryptKey key, byte[] keyData, byte[] iv)
        {
            Key = key;
            KeyBuffer = keyData;
            IV = Globs.CopyData(iv) ?? new byte[BlockSize];
        }

        public byte[] KeyData { get { return KeyBuffer; } }

        public int BlockSize { get { return 16; } }

#elif !WINDOWS_UWP
        private readonly SymmetricAlgorithm Alg;

        public byte[] KeyData { get { return Alg.Key; } }

        /// <summary>
        /// Block size in bytes.
        /// </summary>
        public int BlockSize { get { return Alg.BlockSize / 8; } }

        private SymmCipher(SymmetricAlgorithm alg)
        {
            Alg = alg;
        }
#else // WINDOWS_UWP
        private CryptographicKey Key;
        private byte[] KeyBuffer;
        private byte[] IV;

        private SymmCipher(CryptographicKey key, byte[] keyData, byte[] iv)
        {
            Key = key;
            KeyBuffer = keyData;
            IV = Globs.CopyData(iv) ?? new byte[BlockSize];
        }

        public byte[] KeyData { get { return KeyBuffer; } }

        public int BlockSize { get { return 16; } }
#endif // !TSS_USE_BCRYPT && WINDOWS_UWP

        public static int GetBlockSize(SymDefObject symDef)
        {
            if (symDef.Algorithm == TpmAlgId.Tdes)
            {
                return 8;
            }
            if (symDef.Algorithm != TpmAlgId.Aes)
            {
                Globs.Throw<ArgumentException>("Unsupported algorithm " + symDef.Algorithm);
                return 0;
            }
            return 16;
        }

        /// <summary>
        /// Create a new SymmCipher object with a random key based on the alg and mode supplied.
        /// </summary>
        /// <param name="algId"></param>
        /// <param name="numBits"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static SymmCipher Create(SymDefObject symDef = null, byte[] keyData = null, byte[] iv = null)
        {
            if (symDef == null)
            {
                symDef = new SymDefObject(TpmAlgId.Aes, 128, TpmAlgId.Cfb);
            }

#if TSS_USE_BCRYPT
            BCryptAlgorithm alg = null;

            switch (symDef.Algorithm)
            {
                case TpmAlgId.Aes:
                    alg = new BCryptAlgorithm(Native.BCRYPT_AES_ALGORITHM);
                    break;
                case TpmAlgId.Tdes:
                    alg = new BCryptAlgorithm(Native.BCRYPT_3DES_ALGORITHM);
                    break;
                default:
                    Globs.Throw<ArgumentException>("Unsupported symmetric algorithm " + symDef.Algorithm);
                    break;
            }

            if (keyData == null)
            {
                keyData = Globs.GetRandomBytes(symDef.KeyBits / 8);
            }
            var key = alg.GenerateSymKey(symDef, keyData, GetBlockSize(symDef));
            //key = BCryptInterface.ExportSymKey(keyHandle);
            //keyHandle = alg.LoadSymKey(key, symDef, GetBlockSize(symDef));
            alg.Close();
            return key == null ? null : new SymmCipher(key, keyData, iv);
#elif !WINDOWS_UWP
            SymmetricAlgorithm alg = null; // = new RijndaelManaged();
            bool limitedSupport = false;
            // DES and __3DES are not supported in TPM 2.0 rev. 0.96 to 1.30
            switch (symDef.Algorithm) {
                case TpmAlgId.Aes:
                    alg = new RijndaelManaged();
                    break;
                case TpmAlgId.Tdes:
                    alg = new TripleDESCryptoServiceProvider();
                    limitedSupport = true;
                    break;
                default:
                    Globs.Throw<ArgumentException>("Unsupported symmetric algorithm " + symDef.Algorithm);
                    break;
            }

            int blockSize = GetBlockSize(symDef);
            alg.KeySize = symDef.KeyBits;
            alg.BlockSize = blockSize * 8;
            alg.Padding = PaddingMode.None;
            alg.Mode = GetCipherMode(symDef.Mode);

            // REVISIT: Get this right for other modes
            if (symDef.Algorithm == TpmAlgId.Tdes && symDef.Mode == TpmAlgId.Cfb)
            {
                alg.FeedbackSize = 8;
            }
            else
            {
                alg.FeedbackSize = alg.BlockSize;
            }

            if (keyData == null)
            {
                // Generate random key
                alg.IV = Globs.GetZeroBytes(blockSize);
                try
                {
                    alg.GenerateKey();
                }
                catch (Exception)
                {
                    alg.Dispose();
                    throw;
                }
            }
            else
            {
                // Use supplied key bits
                alg.Key = keyData;
                if (iv == null)
                {
                    iv = Globs.GetZeroBytes(blockSize);
                }
                else if (iv.Length != blockSize)
                {
                    Array.Resize(ref iv, blockSize);
                }
                alg.IV = iv;
            }

            var symCipher = new SymmCipher(alg);
            symCipher.LimitedSupport = limitedSupport;
            return symCipher;
#else // WINDOWS_UWP
            string algName = "";
            switch (symDef.Algorithm)
            {
                case TpmAlgId.Aes:
                    switch (symDef.Mode)
                    {
                        case TpmAlgId.Cbc:
                            algName = SymmetricAlgorithmNames.AesCbc;
                            break;
                        case TpmAlgId.Ecb:
                            algName = SymmetricAlgorithmNames.AesEcb;
                            break;
                        case TpmAlgId.Cfb:
                            algName = SymmetricAlgorithmNames.AesCbcPkcs7;
                            break;
                        default:
                            Globs.Throw<ArgumentException>("Unsupported mode (" + symDef.Mode + ") for algorithm " + symDef.Algorithm);
                            break;
                    }
                    break;
                case TpmAlgId.Tdes:
                    switch (symDef.Mode)
                    {
                        case TpmAlgId.Cbc:
                            algName = SymmetricAlgorithmNames.TripleDesCbc;
                            break;
                        case TpmAlgId.Ecb:
                            algName = SymmetricAlgorithmNames.TripleDesEcb;
                            break;
                        default:
                            Globs.Throw<ArgumentException>("Unsupported mode (" + symDef.Mode + ") for algorithm " + symDef.Algorithm);
                            break;
                    }
                    break;
                default:
                    Globs.Throw<ArgumentException>("Unsupported symmetric algorithm " + symDef.Algorithm);
                    break;
            }

            if (keyData == null)
            {
                keyData = Globs.GetRandomBytes(symDef.KeyBits / 8);
            }

            SymmetricKeyAlgorithmProvider algProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(algName);
            var key = algProvider.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(keyData));

            return key == null ? null : new SymmCipher(key, keyData, iv);
#endif // WINDOWS_UWP
        }

#if !TSS_USE_BCRYPT && !WINDOWS_UWP
        public static CipherMode GetCipherMode(TpmAlgId cipherMode)
        {
            switch (cipherMode)
            {
                case TpmAlgId.Cfb:
                    return CipherMode.CFB;
                case TpmAlgId.Ofb:
                    return CipherMode.OFB;
                case TpmAlgId.Cbc:
                    return CipherMode.CBC;
                case TpmAlgId.Ecb:
                    return CipherMode.ECB;
                default:
                    Globs.Throw<ArgumentException>("GetCipherMode: Unsupported cipher mode");
                    return CipherMode.ECB; // REVISIT: Used to be able to return none here...
            }
        }
#endif

        public static SymmCipher CreateFromPublicParms(IPublicParmsUnion parms)
        {
            switch (parms.GetUnionSelector())
            {
                case TpmAlgId.Rsa:
                    return Create((parms as RsaParms).symmetric);
                case TpmAlgId.Ecc:
                    return Create((parms as EccParms).symmetric);
                default:
                    Globs.Throw<ArgumentException>("CreateFromPublicParms: Unsupported algorithm");
                    return null;
            }
        }

        public static byte[] Encrypt(SymDefObject symDef, byte[] key, byte[] iv, byte[] dataToEncrypt)
        {
            using (SymmCipher cipher = Create(symDef, key, iv))
            {
                return cipher.Encrypt(dataToEncrypt);
            }
        }

        public static byte[] Decrypt(SymDefObject symDef, byte[] key, byte[] iv, byte[] dataToDecrypt)
        {
            using (SymmCipher cipher = Create(symDef, key, iv))
            {
                return cipher.Decrypt(dataToDecrypt);
            }
        }

        /// <summary>
        /// Performs the TPM-defined CFB encrypt using the associated algorithm.  This routine assumes that 
        /// the integrity value has been prepended.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        public byte[] Encrypt(byte[] data, byte[] iv = null)
        {
            byte[] paddedData;
            int unpadded = data.Length % BlockSize;
            paddedData = unpadded == 0 ? data : Globs.AddZeroToEnd(data, BlockSize - unpadded);
#if TSS_USE_BCRYPT
            paddedData = Key.Encrypt(paddedData, null, iv ?? IV);
#elif !WINDOWS_UWP
            if (iv != null && iv.Length > 0)
            {
                Alg.IV = iv;
            }

            ICryptoTransform enc = Alg.CreateEncryptor();
            using (var outStream = new MemoryStream())
            {
                var s = new CryptoStream(outStream, enc, CryptoStreamMode.Write);
                s.Write(paddedData, 0, paddedData.Length);
                s.FlushFinalBlock();
                paddedData = outStream.ToArray();
            }
#else // WINDOWS_UWP
            IBuffer buf = CryptographicEngine.Encrypt(Key, CryptographicBuffer.CreateFromByteArray(paddedData), CryptographicBuffer.CreateFromByteArray(iv ?? IV));
            CryptographicBuffer.CopyToByteArray(buf, out paddedData);
#endif
            return unpadded == 0 ? paddedData : Globs.CopyData(paddedData, 0, data.Length);
        }

        public byte[] Decrypt(byte[] data, byte[] iv = null)
        {
            byte[] paddedData;
            int unpadded = data.Length % BlockSize;
            paddedData = unpadded == 0 ? data : Globs.AddZeroToEnd(data, BlockSize - unpadded);
#if TSS_USE_BCRYPT
            paddedData = Key.Decrypt(paddedData, null, iv ?? IV);
            return Globs.CopyData(paddedData, 0, data.Length);
#elif !WINDOWS_UWP
            ICryptoTransform dec = Alg.CreateDecryptor();
            using (var outStream = new MemoryStream(paddedData))
            {
                var s = new CryptoStream(outStream, dec, CryptoStreamMode.Read);
                var tempOut = new byte[data.Length];
                int numPlaintextBytes = s.Read(tempOut, 0, data.Length);
                Debug.Assert(numPlaintextBytes == data.Length);
                return tempOut;
            }
#else // WINDOWS_UWP
            IBuffer buf = CryptographicEngine.Decrypt(Key, CryptographicBuffer.CreateFromByteArray(paddedData), CryptographicBuffer.CreateFromByteArray(iv ?? IV));
            CryptographicBuffer.CopyToByteArray(buf, out paddedData);
            return paddedData;
#endif
        }

        /// <summary>
        /// De-envelope inner-wrapped duplication blob.
        /// TODO: Move this to TpmPublic and make it fully general
        /// </summary>
        /// <param name="exportedPrivate"></param>
        /// <param name="encAlg"></param>
        /// <param name="encKey"></param>
        /// <param name="nameAlg"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Sensitive SensitiveFromDuplicateBlob(TpmPrivate exportedPrivate, SymDefObject encAlg, byte[] encKey, TpmAlgId nameAlg, byte[] name)
        {
            byte[] dupBlob = exportedPrivate.buffer;
            byte[] sensNoLen;
            using (SymmCipher c = Create(encAlg, encKey))
            {
                byte[] innerObject = c.Decrypt(dupBlob);
                byte[] innerIntegrity, sensitive;

                KDF.Split(innerObject,
                          16 + CryptoLib.DigestSize(nameAlg) * 8,
                          out innerIntegrity,
                          8 * (innerObject.Length - CryptoLib.DigestSize(nameAlg) - 2),
                          out sensitive);

                byte[] expectedInnerIntegrity = Marshaller.ToTpm2B(CryptoLib.HashData(nameAlg, sensitive, name));

                if (!Globs.ArraysAreEqual(expectedInnerIntegrity, innerIntegrity))
                {
                    Globs.Throw("SensitiveFromDuplicateBlob: Bad inner integrity");
                }

                sensNoLen = Marshaller.Tpm2BToBuffer(sensitive);
            }
            var sens = Marshaller.FromTpmRepresentation<Sensitive>(sensNoLen);
            return sens;
        }

        public void Dispose()
        {
#if TSS_USE_BCRYPT
            Key.Dispose();
#elif !WINDOWS_UWP
            Alg.Dispose();
#endif
        }
    }
}
