using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GetDirSyncChanges
{
    public static class AttributeSyntax
    {

        //https://docs.microsoft.com/en-us/windows/win32/adsi/mapping-active-directory-syntax-to-adsi-syntax
        /// <summary>
        /// 2.5.5.1
        /// </summary>
        public const string DistinguishedName = "2.5.5.1";

        /// <summary>
        /// 2.5.5.2
        /// </summary>
        public const string Oid = "2.5.5.2";
        /// <summary>
        /// 2.5.5.3
        /// </summary>
        public const string CaseSensitiveString = "2.5.5.3";

        /// <summary>
        /// 2.5.5.4
        /// </summary>
        public const string CaseIgnoreString = "2.5.5.4";

        /// <summary>
        /// 2.5.5.5
        /// </summary>
        public const string IA5String = "2.5.5.5";                 // aka Print Case String

        /// <summary>
        /// 2.5.5.6
        /// </summary>
        public const string NumericString = "2.5.5.6";

        /// <summary>
        /// 2.5.5.7
        /// </summary>
        public const string ORName = "2.5.5.7";

        /// <summary>
        /// 2.5.5.7
        /// </summary>
        public const string DNwithBinary = "2.5.5.7";

        /// <summary>
        /// 2.5.5.8
        /// </summary>
        public const string Boolean = "2.5.5.8";

        /// <summary>
        /// 2.5.5.9
        /// </summary>
        public const string Integer = "2.5.5.9";

        /// <summary>
        /// 2.5.5.10
        /// </summary>
        public const string OctetString = "2.5.5.10";

        /// <summary>
        /// 2.5.5.11
        /// </summary>
        public const string Time = "2.5.5.11";

        /// <summary>
        /// 2.5.5.12
        /// </summary>
        public const string String = "2.5.5.12";

        /// <summary>
        /// 2.5.5.13
        /// </summary>
        public const string PresentationAddress = "2.5.5.13";

        //public const string DistnameAddress = "2.5.5.14";

        /// <summary>
        /// 2.5.5.15
        /// </summary>
        public const string SecurityDescriptor = "2.5.5.15";

        /// <summary>
        /// 2.5.5.16
        /// </summary>
        public const string LargeInteger = "2.5.5.16";

        /// <summary>
        /// 2.5.5.17
        /// </summary>
        public const string Sid = "2.5.5.17";


        /// <summary>
        /// 2.5.5.14
        /// </summary>
        public const string DNWithString = "2.5.5.14";
    }
}
