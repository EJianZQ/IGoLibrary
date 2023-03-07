using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace IGoLibrary_Winform.Crypt
{
    class Decrypt
    {
        private static byte[] Keys = { 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF };
        public static string DES(string decryptString, string decryptKey)//解密
        {
            try
            {
                byte[] rgbKey = Encoding.UTF8.GetBytes(decryptKey);
                byte[] rgbIV = Keys;
                byte[] inputByteArray = Convert.FromBase64String(decryptString);
                DESCryptoServiceProvider DCSP = new DESCryptoServiceProvider();
                MemoryStream mStream = new MemoryStream();
                CryptoStream cStream = new CryptoStream(mStream, DCSP.CreateDecryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();
                return Encoding.UTF8.GetString(mStream.ToArray());
            }
            catch
            {
                return "解密失败";//如果密钥不足8位或解密出错直接返回错误提示
            }
        }

        public static string DES(string decryptString, string decryptKey, UInt32 times)//多重解密
        {
            string[] result = new string[times + 1];
            result[0] = decryptString;
            try
            {
                for (UInt32 i = 1; i < times + 1; i++)
                {
                    //Console.WriteLine(i);
                    result[i] = Decrypt.DES(result[i - 1], decryptKey);
                }
                return result[times];
            }
            catch
            {
                return "解密失败";//如果密钥不足8位或解密出错直接返回错误提示
            }
        }
    }
}
