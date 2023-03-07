using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace IGoLibrary_Winform.Crypt
{
    class Encrypt
    {
        private static byte[] Keys = { 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF };
        public static string DES(string encryptString, string encryptKey)//加密
        {
            try
            {
                byte[] rgbKey = Encoding.UTF8.GetBytes(encryptKey.Substring(0, 8));//转换为字节
                byte[] rgbIV = Keys;
                byte[] inputByteArray = Encoding.UTF8.GetBytes(encryptString);//将明文转换成字节
                DESCryptoServiceProvider dCSP = new DESCryptoServiceProvider();//实例化数据加密标准
                MemoryStream mStream = new MemoryStream();//实例化内存流
                //将数据流链接到加密转换的流
                CryptoStream cStream = new CryptoStream(mStream, dCSP.CreateEncryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();//清除缓冲流
                return Convert.ToBase64String(mStream.ToArray());
            }
            catch
            {
                return "加密失败";//如果密钥不足8位或解密出错直接返回错误提示
            }
        }

        public static string DES(string encryptString, string encryptKey, UInt32 times)//多重加密
        {
            string[] result = new string[times + 1];
            result[0] = encryptString;
            try
            {
                for (UInt32 i = 1; i < times + 1; i++)
                {
                    //Console.WriteLine(i);
                    result[i] = Encrypt.DES(result[i - 1], encryptKey);
                }
                return result[times];
            }
            catch
            {
                return "加密失败";//如果密钥不足8位或解密出错直接返回错误提示
            }
        }
    }
}
