using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.DirectoryServices.Protocols;


namespace GetDirSyncChanges
{
    static public class Metadata
    {

        // this contains just attribute metadata
        // key is attribute
        public static Dictionary<string, msDSMetaData> adMetadataAttribute = new Dictionary<string, msDSMetaData>(StringComparer.OrdinalIgnoreCase);


        // this contains just value metadata
        // key is attribute+objectDN
        public static Dictionary<string, msDSMetaData> adMetadataValue = new Dictionary<string, msDSMetaData>(StringComparer.OrdinalIgnoreCase);


        public class msDSMetaData
        {

            public string dataType;
            public string attribute;
            public DateTime lastOriginatingChange;
            public int version;
            public string invocationGuid;
            public string DSA;
            public string dsaDcNameOnly;
            public long localUSN;
            public long sourceUSN;

            // specific to Value data
            public string objectDN = "";
            public DateTime timeCreated;
            public DateTime timeDeleted;
        }


        public static void GetMetaData(LdapConnection ldapConnection, string objectDN)
        {

            adMetadataAttribute.Clear();
            adMetadataValue.Clear();


            SearchRequest searchRequest = new SearchRequest(objectDN, $"(objectClass=*)", SearchScope.Base, new string[] { "msDS-ReplAttributeMetaData;binary", "msDS-ReplValueMetaData;binary" });
            searchRequest.Controls.Add(new ShowDeletedControl());
            SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);

            SearchResultEntry entry = searchResponse.Entries[0];

            if (entry.Attributes.Contains("msDS-ReplAttributeMetaData;binary"))
            {

                foreach (byte[] byteArray in entry.Attributes["msDS-ReplAttributeMetaData;binary"].GetValues(typeof(byte[])))
                {
                    DecodeMetaData("attribute", byteArray, objectDN);
                }
            }

            // do we have any Value metadata ?
            if (entry.Attributes.Contains("msDS-ReplValueMetaData;binary"))
            {
                List<byte[]> allValueData = new List<byte[]>();

                // if we have more than 1000 values, will also have msDS-ReplValueMetaData;binary;range=0-999
                // so will need to do paging

                // no paging required
                if (!entry.Attributes.Contains("msDS-ReplValueMetaData;binary;range=0-999"))
                {
                    foreach (byte[] byteArray in entry.Attributes["msDS-ReplValueMetaData;binary"].GetValues(typeof(byte[])))
                    {
                        allValueData.Add(byteArray);
                    }
                }

                // paging required
                if (entry.Attributes.Contains("msDS-ReplValueMetaData;binary;range=0-999"))
                {

                    // add the first page as we already have the values
                    foreach (byte[] byteArray in entry.Attributes["msDS-ReplValueMetaData;binary;range=0-999"].GetValues(typeof(byte[])))
                    {
                        allValueData.Add(byteArray);
                    }

                    // now get remaining pages
                    int RangeStep = 999;
                    int LowRange = 1000;                        // we're starting for next query e.g. range=1000-1999
                    int HighRange = LowRange + RangeStep;


                    while (true)
                    {
                        searchRequest.Attributes.Clear();
                        searchRequest.Attributes.Add($"msDS-ReplValueMetaData;binary;range={LowRange}-{HighRange}");
                        SearchResponse RangeSearchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest, new TimeSpan(0, 2, 0));


                        if (RangeSearchResponse.Entries[0].Attributes.Contains($"msDS-ReplValueMetaData;binary;range={LowRange}-{HighRange}"))
                        {
                            foreach (byte[] byteArray in RangeSearchResponse.Entries[0].Attributes[$"msDS-ReplValueMetaData;binary;range={LowRange}-{HighRange}"].GetValues(typeof(byte[])))
                            {
                                allValueData.Add(byteArray);
                            }

                            LowRange = HighRange + 1;
                            HighRange = LowRange + RangeStep;
                        }
                        else
                        {
                            // last query
                            searchRequest.Attributes.Clear();
                            searchRequest.Attributes.Add($"msDS-ReplValueMetaData;binary;range={LowRange}-*");

                            SearchResponse LastRangeSearchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest, new TimeSpan(0, 2, 0));
                            if (LastRangeSearchResponse.Entries.Count == 1)
                            {
                                foreach (byte[] byteArray in LastRangeSearchResponse.Entries[0].Attributes[$"msDS-ReplValueMetaData;binary;range={LowRange}-*"].GetValues(typeof(byte[])))
                                {
                                    allValueData.Add(byteArray);
                                }
                            }
                            break;
                        }
                    }
                }

                foreach (byte[] byteArray in allValueData)
                {
                    DecodeMetaData("value", byteArray, objectDN);
                }
            }
        }



        static void DecodeMetaData(string dataType, byte[] byteArray, string objectBeingProccessed)
        {

            if (dataType == "attribute")
            {

                Int32 attributeOffset = BitConverter.ToInt32(byteArray, 0);
                Int32 version = BitConverter.ToInt32(byteArray, 4);
                DateTime ftLastOriginatingChange = DateTime.FromFileTimeUtc(BitConverter.ToInt64(byteArray, 8));
                Guid invocationGuid = new Guid(byteArray.Skip(16).Take(16).ToArray());
                ulong sourceUsn = BitConverter.ToUInt64(byteArray, 32);
                ulong localUsn = BitConverter.ToUInt64(byteArray, 40);
                Int32 DSAOffset = BitConverter.ToInt32(byteArray, 48);
                string attribute = "";
                if (DSAOffset > 0)
                {
                    attribute = Encoding.Unicode.GetString(byteArray.Skip(attributeOffset).Take(DSAOffset - attributeOffset).ToArray()).Trim('\0');
                }
                else
                {
                    attribute = Encoding.Unicode.GetString(byteArray.Skip(attributeOffset).ToArray()).Trim('\0');
                }

                string dsa = "";
                string dsadcnameonly = "";
                if (DSAOffset != 0)
                {
                    dsa = Encoding.Unicode.GetString(byteArray.Skip(DSAOffset).ToArray()).Trim('\0');
                    string[] tmpArray = dsa.Split(',');
                    dsadcnameonly = tmpArray[1].Replace("CN=", "");
                }

                msDSMetaData m = new msDSMetaData();
                m.attribute = attribute;
                m.version = version;
                m.dataType = "attribute";
                m.DSA = dsa;
                m.dsaDcNameOnly = dsadcnameonly;
                m.invocationGuid = invocationGuid.ToString();
                m.lastOriginatingChange = ftLastOriginatingChange;
                m.localUSN = (long)localUsn;
                m.sourceUSN = (long)sourceUsn;


                adMetadataAttribute.Add(m.attribute, m);

            }


            if (dataType == "value")
            {


                Int32 attributeOffset = BitConverter.ToInt32(byteArray, 0);
                Int32 objectDNOffset = BitConverter.ToInt32(byteArray, 4);
                Int32 cbDataLength = BitConverter.ToInt32(byteArray, 8);
                Int32 pbDataOffset = BitConverter.ToInt32(byteArray, 12);
                DateTime ftTimeDeleted = DateTime.FromFileTimeUtc(BitConverter.ToInt64(byteArray, 16));
                DateTime ftTimeCreated = DateTime.FromFileTimeUtc(BitConverter.ToInt64(byteArray, 24));
                Int32 version = BitConverter.ToInt32(byteArray, 32);
                DateTime ftLastOriginatingChange = DateTime.FromFileTimeUtc(BitConverter.ToInt64(byteArray, 36));
                Guid invocationGuid = new Guid(byteArray.Skip(44).Take(16).ToArray());
                ulong sourceUsn = BitConverter.ToUInt64(byteArray, 64);
                ulong localUsn = BitConverter.ToUInt64(byteArray, 72);
                Int32 DSAOffset = BitConverter.ToInt32(byteArray, 80);
                string attribute = Encoding.Unicode.GetString(byteArray.Skip(attributeOffset).Take(objectDNOffset - attributeOffset).ToArray()).Trim('\0');
                string objectDN = "";
                if (DSAOffset > 0)
                {
                    objectDN = Encoding.Unicode.GetString(byteArray.Skip(objectDNOffset).Take(DSAOffset - objectDNOffset).ToArray()).Trim('\0');
                }
                else
                {
                    objectDN = Encoding.Unicode.GetString(byteArray.Skip(objectDNOffset).ToArray()).Trim('\0');
                }

                string dsa = "";
                string dsadcnameonly = "";
                if (DSAOffset != 0)
                {
                    dsa = Encoding.Unicode.GetString(byteArray.Skip(DSAOffset).ToArray()).Trim('\0');

                    string[] tmpArray = dsa.Split(',');
                    dsadcnameonly = tmpArray[1].Replace("CN=", "");
                }

                msDSMetaData m = new msDSMetaData();
                m.dataType = "value";
                m.attribute = attribute;
                m.DSA = dsa;
                m.dsaDcNameOnly = dsadcnameonly;
                m.invocationGuid = invocationGuid.ToString();
                m.lastOriginatingChange = ftLastOriginatingChange;
                m.localUSN = (long)localUsn;
                m.objectDN = objectDN;
                m.sourceUSN = (long)sourceUsn;
                m.timeCreated = ftTimeCreated;
                m.timeDeleted = ftTimeDeleted;
                m.version = version;
                m.invocationGuid = invocationGuid.ToString();


                try
                {
                    adMetadataValue.Add(m.attribute + m.objectDN, m);
                }
                catch (Exception err)
                {
                    Console.WriteLine($"{err.Message} adMetadataValue Count={adMetadataValue.Count} {objectBeingProccessed}  attribute={m.attribute} objectDN={m.objectDN}");

                }

            }


        }
    }
}
