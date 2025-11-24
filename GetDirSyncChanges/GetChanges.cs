using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.DirectoryServices.Protocols;
using System.IO;
using System.Threading;
using System.Security.Principal;
using System.ServiceProcess;

namespace GetDirSyncChanges
{
    class GetChanges
    {

        static LdapConnection ldapConnection = null;
        static string defaultNamingContext = "";
        static string schemaNamingContext = "";
        static string configurationNamingContext = "";
        static List<string> namingContexts = new List<string>();

        static Dictionary<string, string> attributeSyntax = new Dictionary<string, string>();

        static int delay = 1;

        static System.IO.StreamWriter LogFileHandle = null;

        static bool SaveCookiesToDisk = false;
        static bool SaveLogFile = false;

        // ncName is key
        static Dictionary<string, byte[]> CookiesInMemory = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);


        static bool ShowChangedAttributes = false;

        static void Main(string[] args)
        {
            GetCommandLine(args);

            LookForChanges();
        }

        static void GetCommandLine(string[] args)
        {
            /* commandline options
            * -server:     
            * -user:       could be domain\user or user
            * -password:   
            * -savecookies
            * -savelogfile
            * 
            */


            if (args.Count() < 3)
            {
                // need minimum of server, user and password 

                Console.WriteLine(@"GetDirSyncChanges -server:<domain controller name or ip>  -user:<domain\user or user> -password:<password> -savecookies -savelogfile");

                Console.Write("server :");
                string s = Console.ReadLine();

                Console.Write("user: ");
                string u = Console.ReadLine();

                Console.Write("password: ");
                string p = ReadPasswordMasked();
                Console.WriteLine();


                if (s.Trim() != "")
                {
                    Connection.server = s.Trim();
                }
                else
                {
                    Environment.Exit(1);
                }

                if (u.Trim() != "")
                {
                    Connection.username = u.Trim();
                }

                if (p.Trim() != "")
                {
                    Connection.password = p.Trim();
                }


                Console.WriteLine("Save cookies to disk ? Y/N: ");
                ConsoleKeyInfo kk = Console.ReadKey(true);

                if (kk.Key == ConsoleKey.Y)
                {
                    SaveCookiesToDisk = true;
                }


                Console.WriteLine("Save log file to disk ? Y/N: ");
                ConsoleKeyInfo kkk = Console.ReadKey(true);

                if (kkk.Key == ConsoleKey.Y)
                {
                    SaveLogFile = true;
                }


            }
            else
            {

                // command line  waith 3 or more parameters 
                try
                {

                    foreach (string arg in args)
                    {
                        

                        if (arg.ToLower().Contains("-server:"))
                        {
                            Connection.server = arg.Trim().ToLower().Replace("-server:", "");
                        }

                        if (arg.ToLower().Contains("-user:"))
                        {
                            string u = arg.Trim().Replace("-user:", "");

                            if (u.Contains("\\"))
                            {
                                Connection.domain = u.Substring(0, u.IndexOf('\\'));
                                Connection.username = u.Substring(u.IndexOf('\\') + 1);
                            }
                            else
                            {
                                Connection.username = u;
                            }

                        }

                        if (arg.ToLower().Contains("-password:"))
                        {
                            Connection.password = arg.Trim().Replace("-password:", "");
                        }

                        if (string.IsNullOrEmpty(Connection.username))
                        {
                            Connection.username = null;
                            Connection.password = null;
                        }

                        if (arg.ToLower().Contains("-savecookies"))
                        {
                            SaveCookiesToDisk = true;
                            Console.WriteLine("Cookies will be saved");
                        }

                        if (arg.ToLower().Contains("-savelogfile"))
                        {
                            SaveLogFile = true;
                            Console.WriteLine("Logs will be saved");
                        }

                    }


                    if (Connection.server.Trim() == "")
                    {
                        Console.WriteLine("Invalid server");
                        Environment.Exit(1);
                    }
                }
                catch
                {
                    Console.WriteLine("Error in commandline");
                    Environment.Exit(1);
                }

            }

            Console.Title = $"GetDirSyncChanges {Connection.server}";
                       
        }


        static void LookForChanges()
        {
            OpenLogFile();
            OpenLdapConnection();
            GetRootDSE();
            GetAttributeSyntax();

            InitialSync();

            Console.WriteLine("In delta sync");

            Console.WriteLine("press P   to pause");
            Console.WriteLine("      R   to resume");
            Console.WriteLine("      +   to increase sync interval");
            Console.WriteLine("      -   to decrease sync interval");
            Console.WriteLine("      A   to show changed attributes");
            Console.WriteLine("      Esc to exit");

            bool cancelled = false;

            while (!cancelled)
            {
                foreach (string namingContext in namingContexts)
                {
                    DeltaSync(namingContext);
                }


                for (int i = 0; i < 10; i++)
                {

                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo k = Console.ReadKey(true);

                        if (k.Key == ConsoleKey.Escape)
                        {
                            cancelled = true;
                        }

                        if (k.Key == ConsoleKey.P)
                        {

                            Console.WriteLine("\r\n**Paused**       press R to resume");
                            do
                            {
                                Thread.Sleep(100);
                            }
                            while (Console.ReadKey(true).Key != ConsoleKey.R);
                            Console.WriteLine("**Resumed**");
                        }

                        if (k.Key == ConsoleKey.Add)
                        {
                            delay++;
                            Console.WriteLine($"Delay is set to  {delay} secs");
                        }

                        if (k.Key == ConsoleKey.Subtract)
                        {
                            if (delay > 1)
                            {
                                delay--;
                                Console.WriteLine($"Delay is set to  {delay} secs");
                            }
                        }


                        if (k.Key == ConsoleKey.A)
                        {
                            ShowChangedAttributes = !ShowChangedAttributes;
                            if (ShowChangedAttributes)
                            {
                                Console.WriteLine($"dirSync attributes will be shown");
                            }
                            else
                            {
                                Console.WriteLine($"dirSync attributes will not be shown");
                            }
                        }

                    }

                    Thread.Sleep(delay * 100);      // in this loop 10 times hence 100 and not 1000 !
                }

            }


            CloseLogFile();
        }

        static void DeltaSync(string namingContext)
        {
            List<SearchResultEntry> results = new List<SearchResultEntry>();
            bool bMoreData = true;


            byte[] cookie = RestoreRawCookie(namingContext);


            while (true)
            {
                OpenLdapConnection();

                try
                {

                    SearchRequest searchRequest = new SearchRequest(namingContext, $"(objectClass=*)", System.DirectoryServices.Protocols.SearchScope.Subtree, null);

                    DirSyncRequestControl dirSyncRC = new DirSyncRequestControl(cookie, System.DirectoryServices.Protocols.DirectorySynchronizationOptions.ObjectSecurity | System.DirectoryServices.Protocols.DirectorySynchronizationOptions.IncrementalValues | System.DirectoryServices.Protocols.DirectorySynchronizationOptions.ParentsFirst, Int32.MaxValue);
                    searchRequest.Controls.Add(dirSyncRC);


                    SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);


                    foreach (SearchResultEntry entry in searchResponse.Entries)
                    {
                        results.Add(entry);
                    }


                    foreach (DirectoryControl control in searchResponse.Controls)
                    {
                        if (control is DirSyncResponseControl)
                        {
                            DirSyncResponseControl dsrc = control as DirSyncResponseControl;
                            cookie = dsrc.Cookie;
                            bMoreData = dsrc.MoreData;
                            break;
                        }
                    }

                    dirSyncRC.Cookie = cookie;


                    if (!bMoreData) break;

                }
                catch (Exception err)
                {
                    Console.WriteLine($"Search error {err.Message}");
                    if (Console.KeyAvailable)
                    {
                        if (Console.ReadKey().Key == ConsoleKey.Escape)
                        {
                            Environment.Exit(1);
                        }
                    }
                    Thread.Sleep(1000);
                }
            }



            try
            {

                foreach (SearchResultEntry entry in results)
                {

                    string objectGuid = ShowOctetString(((byte[])entry.Attributes["objectGuid"][0]));

                    bool created = false;
                    bool deleted = false;
                    bool movedOrRenamed = false;
                    bool restored = false;

                    if (ShowChangedAttributes)
                    {

                        foreach (string attribute in entry.Attributes.AttributeNames)
                        {
                            Log($"dirSync attribute: {attribute} count={entry.Attributes[attribute].Count}");
                        }
                    }




                    if (!entry.DistinguishedName.Contains("DEL:") && entry.Attributes.Contains("objectClass") && entry.Attributes.Contains("objectCategory"))
                    {
                        created = true;
                    }


                    if ((entry.DistinguishedName.ToLower().Contains("del:") || entry.DistinguishedName.ToLower().Contains("cn=deleted objects")))
                    {
                        deleted = true;
                    }


                    if (!entry.DistinguishedName.Contains("DEL:") && !entry.DistinguishedName.Contains("CNF:") && !entry.Attributes.Contains("objectClass") && entry.Attributes.Contains("name") && entry.Attributes.Contains("parentGuid"))
                    {
                        // name and parentGuid get updated for both rename or move
                        // therefore we don't know exactly which operation was perfomed without getting extra data
                        movedOrRenamed = true;
                    }

                    if (entry.Attributes.Contains("isDeleted") && entry.Attributes["isDeleted"].Count == 0)
                    {
                        restored = true;
                    }


                    SearchResultAttributeCollection attributes = entry.Attributes;

                    Metadata.GetMetaData(ldapConnection, entry.DistinguishedName);


                    if (created)
                    {
                        Log($"{entry.DistinguishedName} CREATED", ConsoleColor.Green, ConsoleColor.White);
                    }
                    else if (deleted)
                    {
                        Log($"{entry.DistinguishedName} DELETED", ConsoleColor.Red, ConsoleColor.White);
                    }
                    else if (restored)
                    {
                        Log($"{entry.DistinguishedName} RESTORED", ConsoleColor.DarkRed, ConsoleColor.White);
                    }
                    else if (movedOrRenamed)
                    {
                        Log($"{entry.DistinguishedName} MOVED OR RENAMED", ConsoleColor.Magenta, ConsoleColor.White);
                    }
                    else
                    {
                        Log($"{entry.DistinguishedName} UPDATED", ConsoleColor.Blue, ConsoleColor.White);
                    }



                    foreach (DirectoryAttribute attribute in attributes.Values)
                    {

                        string attributeDisplayName = attribute.Name.Replace(";range=0-0", "").Replace(";range=1-1", "");        // this is the attribute we will display in console output


                        int version = 0;
                        long sourceUSN = 0;
                        long localUSN = 0;
                        string dsa = "";

                        if (Metadata.adMetadataAttribute.ContainsKey(attributeDisplayName))
                        {
                            version = Metadata.adMetadataAttribute[attributeDisplayName].version;
                            sourceUSN = Metadata.adMetadataAttribute[attributeDisplayName].sourceUSN;
                            localUSN = Metadata.adMetadataAttribute[attributeDisplayName].localUSN;
                            dsa = Metadata.adMetadataAttribute[attributeDisplayName].dsaDcNameOnly;
                        }



                        if (entry.Attributes[attribute.Name].Count == 0)
                        {
                            switch (attribute.Name)
                            {
                                // these will not have a displayable value, but have been changed
                                case "lmPwdHistory":
                                case "dBCSPwd":
                                case "unicodePwd":
                                case "supplementalCredentials":
                                case "ntPwdHistory":

                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, "set");
                                    break;

                                // its displayable, but no value-->cleared
                                default:

                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, "cleared");
                                    break;

                            }

                        }
                        else
                        {

                            switch (attribute.Name)
                            {
                                case "objectGUID":
                                    if (created)
                                    {
                                        Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, objectGuid.ToString());
                                    }
                                    break;

                                case "instanceType":
                                    if (created)
                                    {
                                        Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, (string)entry.Attributes[attribute.Name][0]);
                                    }
                                    break;

                                case "name":

                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, ((string)entry.Attributes[attribute.Name][0]).Replace("\n", @"\0A"));
                                    break;


                                default:

                                    string _attributeSyntax = attributeSyntax[attribute.Name.Replace(";range=0-0", "").Replace(";range=1-1", "")];

                                    switch (_attributeSyntax)
                                    {
                                        case AttributeSyntax.DistinguishedName:

                                            for (int i = 0; i < entry.Attributes[attribute.Name].Count; i++)
                                            {
                                                string objectDN = (string)entry.Attributes[attribute.Name][i];

                                                if (Metadata.adMetadataValue.ContainsKey(attributeDisplayName + objectDN))
                                                {
                                                    Metadata.msDSMetaData m = Metadata.adMetadataValue[attributeDisplayName + objectDN];
                                                    version = m.version;
                                                    sourceUSN = m.sourceUSN;
                                                    localUSN = m.localUSN;
                                                    dsa = m.dsaDcNameOnly;
                                                }



                                                if (attribute.Name.Contains(";range=0-0"))
                                                {

                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, "--" + (string)entry.Attributes[attribute.Name][i], ConsoleColor.DarkYellow, ConsoleColor.White);
                                                }
                                                else
                                                {
                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, "++" + (string)entry.Attributes[attribute.Name][i], ConsoleColor.DarkCyan, ConsoleColor.White);
                                                }
                                            }

                                            break;

                                        case AttributeSyntax.Sid:
                                            {
                                                byte[][] data = (byte[][])entry.Attributes[attribute.Name].GetValues(typeof(byte[]));
                                                foreach (byte[] b in data)
                                                {
                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, sidAsString(b));
                                                }
                                            }
                                            break;

                                        case AttributeSyntax.OctetString:
                                            {
                                                byte[][] data = (byte[][])entry.Attributes[attribute.Name].GetValues(typeof(byte[]));
                                                foreach (byte[] b in data)
                                                {
                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, ShowOctetString(b));
                                                }
                                            }
                                            break;

                                        case AttributeSyntax.SecurityDescriptor:

                                            Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, ShowOctetString((byte[])entry.Attributes[attribute.Name][0]));
                                            break;


                                        case AttributeSyntax.LargeInteger:
                                            {
                                                string[] stringValues = (string[])entry.Attributes[attribute.Name].GetValues(typeof(string));

                                                foreach (string s in stringValues)
                                                {
                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, LongToDate(s));
                                                }
                                            }
                                            break;


                                        default:
                                            {
                                                string[] stringValues = (string[])entry.Attributes[attribute.Name].GetValues(typeof(string));

                                                foreach (string s in stringValues)
                                                {
                                                    Log(attributeDisplayName, version, sourceUSN, localUSN, dsa, s);
                                                }
                                            }


                                            break;

                                    }


                                    break;

                            }

                        }

                    }

                    Log("");

                }

            }
            catch (Exception err)
            {
                Console.WriteLine($"Processing error {err.Message}");
            }

            SaveRawCookie(namingContext, cookie);
        }


        static void InitialSync()
        {
            bool bMoreData = true;
            byte[] cookie = null;


            ldapConnection.Timeout = new TimeSpan(3, 0, 0);

            foreach (string namingContext in namingContexts)
            {

                if (SaveCookiesToDisk && File.Exists($"{namingContext}-cookie.bin"))
                {
                    Console.WriteLine($"Cookie exists for {namingContext}...not performing initial sync");
                    continue;
                }

                Console.WriteLine($"Performing initial sync for partition {namingContext}");

                while (true)
                {
                    SearchRequest searchRequest = new SearchRequest(namingContext, $"(objectClass=nonexistentclass)", SearchScope.Subtree, new string[] { "1.1" });


                    DirSyncRequestControl dirSyncRC = new DirSyncRequestControl(cookie, System.DirectoryServices.Protocols.DirectorySynchronizationOptions.ObjectSecurity, Int32.MaxValue);
                    searchRequest.Controls.Add(dirSyncRC);
                    searchRequest.TypesOnly = true;

                    SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);
                    foreach (DirectoryControl control in searchResponse.Controls)
                    {
                        if (control is DirSyncResponseControl)
                        {
                            DirSyncResponseControl dsrc = control as DirSyncResponseControl;
                            cookie = dsrc.Cookie;
                            bMoreData = dsrc.MoreData;
                            break;
                        }
                    }

                    dirSyncRC.Cookie = cookie;

                    // no more data, break out of loop
                    if (!bMoreData) break;
                }


                SaveRawCookie(namingContext, cookie);

            }
        }

#if DEBUG
        static void InitialSyncAll()
        {

            // performs an initial sync, but returns all objects

            List<SearchResultEntry> results = new List<SearchResultEntry>();
            bool bMoreData = true;
            byte[] cookie = null;


            ldapConnection.Timeout = new TimeSpan(3, 0, 0);

            foreach (string namingContext in namingContexts)
            {

                while (true)

                {
                    // note return all changed attributes
                    SearchRequest searchRequest = new SearchRequest(namingContext, $"(objectClass=*)", System.DirectoryServices.Protocols.SearchScope.Subtree, null);

                    DirSyncRequestControl dirSyncRC = new DirSyncRequestControl(cookie, System.DirectoryServices.Protocols.DirectorySynchronizationOptions.ObjectSecurity | System.DirectoryServices.Protocols.DirectorySynchronizationOptions.IncrementalValues | System.DirectoryServices.Protocols.DirectorySynchronizationOptions.ParentsFirst, Int32.MaxValue);
                    searchRequest.Controls.Add(dirSyncRC);

                    // all DN's will be extended
                    //searchRequest.Controls.Add(new ExtendedDNControl(ExtendedDNFlag.StandardString));

                    SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);


                    foreach (SearchResultEntry entry in searchResponse.Entries)
                    {
                        results.Add(entry);
                    }


                    foreach (DirectoryControl control in searchResponse.Controls)
                    {
                        if (control is DirSyncResponseControl)
                        {
                            DirSyncResponseControl dsrc = control as DirSyncResponseControl;
                            cookie = dsrc.Cookie;
                            bMoreData = dsrc.MoreData;
                            break;
                        }
                    }

                    dirSyncRC.Cookie = cookie;

                  
                    if (!bMoreData) break;

                }


                // process changes
                foreach (SearchResultEntry entry in results)
                {
                    bool created = false;
                    // created
                    if (!entry.DistinguishedName.Contains("DEL:") && entry.Attributes.Contains("objectClass") && entry.Attributes.Contains("objectCategory"))
                    {
                        created = true;
                    }

                    // renamed or moved
                    if (!entry.DistinguishedName.Contains("DEL:") && !entry.DistinguishedName.Contains("CNF:") && !entry.Attributes.Contains("objectClass") && entry.Attributes.Contains("name") && entry.Attributes.Contains("parentGuid"))
                    {
                        // name and parentGuid get updated for both rename or move
                        // there we don't know exactly which operation was perfomed without getting extra data

                        Log(entry.DistinguishedName + " renamed or moved");
                    }

                    // deleted
                    else if ((entry.DistinguishedName.ToLower().Contains("del:") || entry.DistinguishedName.ToLower().Contains("cn=deleted objects")))
                    {
                        Log(entry.DistinguishedName + " deleted");
                    }

                    // attribute updated
                    else
                    {
                        SearchResultAttributeCollection attributes = entry.Attributes;

                        Log($"{entry.DistinguishedName}  {(created ? " CREATED" : "")} ");

                        foreach (DirectoryAttribute attribute in attributes.Values)
                        {

                            string attributeDisplayName = attribute.Name;       // this is the attribute we will display in console output
                            attributeDisplayName = attributeDisplayName.Replace(";range=0-0", "").Replace(";range=1-1", "");

                            if (attributeDisplayName.Length < 40)
                            {
                                attributeDisplayName = attributeDisplayName + new string(' ', 40 - attributeDisplayName.Length);
                            }

                            if (entry.Attributes[attribute.Name].Count == 0)
                            {
                                switch (attribute.Name)
                                {
                                    // these will not have a displayable value, but have been changed
                                    case "lmPwdHistory":
                                    case "dBCSPwd":
                                    case "unicodePwd":
                                    case "supplementalCredentials":
                                    case "ntPwdHistory":
                                        Log($" {attributeDisplayName} : set");
                                        break;

                                    default:
                                        Log($" {attributeDisplayName} : cleared");
                                        break;

                                }

                            }
                            else
                            {

                                switch (attribute.Name)
                                {
                                    case "objectGUID":
                                        break;

                                    case "instanceType":
                                        break;


                                    default:

                                        string _attributeSyntax = attributeSyntax[attribute.Name.Replace(";range=0-0", "").Replace(";range=1-1", "")];

                                        switch (_attributeSyntax)
                                        {
                                            case AttributeSyntax.DistinguishedName:

                                                for (int i = 0; i < entry.Attributes[attribute.Name].Count; i++)
                                                {
                                                    if (attribute.Name.Contains(";range=0-0"))
                                                    {
                                                        Log($" {attributeDisplayName} : --{(string)entry.Attributes[attribute.Name][i]}");
                                                    }
                                                    else
                                                    {
                                                        Log($" {attributeDisplayName} : ++{(string)entry.Attributes[attribute.Name][i]}");
                                                    }
                                                }

                                                break;

                                            case AttributeSyntax.Sid:
                                                // SDSP may cast to string by default
                                                {
                                                    byte[][] data = (byte[][])entry.Attributes[attribute.Name].GetValues(typeof(byte[]));
                                                    foreach (byte[] b in data)
                                                    {
                                                        Log($" {attributeDisplayName} : {sidAsString(b)}");
                                                        //Log($" {attribute.Name} : {sidAsString((byte[])entry.Attributes[attribute.Name][0])}");
                                                    }
                                                }
                                                break;

                                            case AttributeSyntax.OctetString:
                                                // SDSP may cast to string by default
                                                {
                                                    byte[][] data = (byte[][])entry.Attributes[attribute.Name].GetValues(typeof(byte[]));
                                                    foreach (byte[] b in data)
                                                    {
                                                        Log($" {attributeDisplayName} : {ShowOctetString(b)}");
                                                        //Log($" {attribute.Name} : {ShowOctetString((byte[])entry.Attributes[attribute.Name][0])}");
                                                    }
                                                }
                                                break;

                                            case AttributeSyntax.SecurityDescriptor:
                                                Log($" {attributeDisplayName} : {ShowOctetString((byte[])entry.Attributes[attribute.Name][0])}");
                                                break;


                                            case AttributeSyntax.LargeInteger:
                                                {
                                                    string[] stringValues = (string[])entry.Attributes[attribute.Name].GetValues(typeof(string));

                                                    foreach (string s in stringValues)
                                                    {
                                                        Log($" {attributeDisplayName} : {LongToDate(s)}");
                                                    }
                                                }
                                                break;


                                            default:
                                                {
                                                    string[] stringValues = (string[])entry.Attributes[attribute.Name].GetValues(typeof(string));

                                                    foreach (string s in stringValues)
                                                    {
                                                        Log($" {attributeDisplayName} : {s}");
                                                    }
                                                }

                                                //Log($" {attributeDisplayName} : {(string)entry.Attributes[attribute.Name][0]}");
                                                break;

                                        }


                                        break;

                                }

                            }

                        }

                        Log("");
                    }
                }

                SaveRawCookie(namingContext, cookie);
            }


        }
#endif

        static void SaveRawCookie(string namingContext, byte[] cookie)
        {
            if (SaveCookiesToDisk)
            {
                MemoryStream ms = new MemoryStream(cookie);

                using (FileStream file = new FileStream(namingContext + "-cookie.bin", FileMode.Create, System.IO.FileAccess.Write))
                {
                    byte[] bytes = new byte[ms.Length];
                    ms.Read(bytes, 0, (int)ms.Length);
                    file.Write(bytes, 0, bytes.Length);
                    ms.Close();
                }
            }
            else
            {
                if (CookiesInMemory.ContainsKey(namingContext))
                {
                    CookiesInMemory[namingContext] = cookie;
                }
                else
                {
                    CookiesInMemory.Add(namingContext, cookie);
                }
            }

        }


        static byte[] RestoreRawCookie(string namingContext)
        {
            if (SaveCookiesToDisk)
            {
                byte[] bytesRead = { };
                bytesRead = File.ReadAllBytes(namingContext + "-cookie.bin");
                return bytesRead;
            }
            else
            {
                return CookiesInMemory[namingContext];
            }
        }



        static void GetRootDSE()
        {
            OpenLdapConnection();

            while (true)
            {
                try
                {

                    SearchRequest searchRequest = new SearchRequest(null, $"(objectClass=*)", SearchScope.Base, null);
                    SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);

                    SearchResultEntry entry = searchResponse.Entries[0];

                    defaultNamingContext = (string)entry.Attributes["defaultNamingContext"][0];
                    schemaNamingContext = (string)entry.Attributes["schemaNamingContext"][0];
                    configurationNamingContext = (string)entry.Attributes["configurationNamingContext"][0];


                    namingContexts.Clear();
                    foreach (string namingContext in entry.Attributes["namingContexts"].GetValues(typeof(string)))
                    {
                        namingContexts.Add(namingContext);
                    }

                    break;
                }
                catch (Exception err)
                {
                    Console.WriteLine($"Waiting to connect to RootDSE {err.Message}");
                    if (Console.KeyAvailable)
                    {
                        if (Console.ReadKey().Key == ConsoleKey.Escape)
                        {
                            Environment.Exit(1);
                        }
                    }
                    Thread.Sleep(1000);


                }
            }


        }

        public static string sidAsString(byte[] mySid)
        {
            string asStringSid = "";
            try
            {
                SecurityIdentifier objectSidAsByte = new SecurityIdentifier(mySid, 0);
                asStringSid = objectSidAsByte.ToString();
            }
            catch
            {
                // will return ""
            }

            return asStringSid;
        }

        public static string ShowOctetString(byte[] data)
        {
            // will see if this is a guid (check for 16 bytes)
            // if not guid, show 16 bytes of octectstring only as hex string

            if (data.Length == 16)
            {
                Guid objectGuid = new Guid(data);
                return objectGuid.ToString();
            }
            else
            {
                string s = $"[{data.Length}] ";
                for (int i = 0; i < data.Length; i++)
                {
                    s += data[i].ToString("x2");
                    if (i == 15) break;
                }
                return s;
            }
        }

        static void GetAttributeSyntax()
        {

            OpenLdapConnection();

            while (true)
            {
                try
                {

                    SearchRequest searchRequest = new SearchRequest(schemaNamingContext, $"(objectClass=attributeSchema)", SearchScope.OneLevel, new string[] { "ldapDisplayName", "attributeSyntax" });
                    PageResultRequestControl pageRequest = new PageResultRequestControl(1000);
                    searchRequest.Controls.Add(pageRequest);

                    SearchOptionsControl searchOptions = new SearchOptionsControl(System.DirectoryServices.Protocols.SearchOption.DomainScope);
                    searchRequest.Controls.Add(searchOptions);

                    while (true)
                    {

                        SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);


                        foreach (System.DirectoryServices.Protocols.DirectoryControl control in searchResponse.Controls)
                        {
                            if (control is System.DirectoryServices.Protocols.PageResultResponseControl)
                            {
                                pageRequest.Cookie = ((System.DirectoryServices.Protocols.PageResultResponseControl)control).Cookie;
                                break;
                            }
                        }


                        foreach (SearchResultEntry entry in searchResponse.Entries)
                        {

                            string _attributeSyntax = (string)entry.Attributes["attributeSyntax"][0];
                            string ldapDisplayName = (string)entry.Attributes["ldapDisplayName"][0];

                            attributeSyntax.Add(ldapDisplayName, _attributeSyntax);

                        }

                        if (pageRequest.Cookie.Length == 0) break;
                    }

                    break;

                }
                catch (Exception err)
                {
                    Console.WriteLine($"Waiting to get attribute syntax {err.Message}");
                    if (Console.KeyAvailable)
                    {
                        if (Console.ReadKey().Key == ConsoleKey.Escape)
                        {
                            Environment.Exit(1);
                        }
                    }
                    Thread.Sleep(1000);
                }
            }


        }

        static void OpenLdapConnection()
        {


            ldapConnection = new LdapConnection(Connection.server);

            if (!string.IsNullOrEmpty(Connection.username))
            {


                if (string.IsNullOrEmpty(Connection.domain))
                {
                    ldapConnection.Credential = new System.Net.NetworkCredential(Connection.username, Connection.password);
                }
                else
                {
                    ldapConnection.Credential = new System.Net.NetworkCredential(Connection.username, Connection.password, Connection.domain);
                }

            }
        }

        static void Log(string s, ConsoleColor myColour = ConsoleColor.White, ConsoleColor backColour = ConsoleColor.Black)
        {
            Console.ForegroundColor = myColour;
            Console.BackgroundColor = backColour;
            if (s.Trim() != "")
            {
                Console.WriteLine($"{DateTime.Now} {s}");
                if (SaveLogFile) LogFileHandle.WriteLine($"{DateTime.Now} {s}");
            }
            else
            {
                Console.WriteLine();
                if (SaveLogFile) LogFileHandle.WriteLine();
            }
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
        }

        static void Log(string attribute, int version, long sourceUSN, long localUSN, string dsa, string value, ConsoleColor myColour = ConsoleColor.White, ConsoleColor backColour = ConsoleColor.Black)
        {
            Console.ForegroundColor = myColour;
            Console.BackgroundColor = backColour;

            //https://learn.microsoft.com/en-us/dotnet/standard/base-types/composite-formatting

            Console.WriteLine("{0,-20} {1,-40} version={2,-6} sourceUSN={3,-8} localUSN={4,-8} dsa={5,-16} value={6}", DateTime.Now, attribute, version, sourceUSN, localUSN, dsa, value);
            if (SaveLogFile) LogFileHandle.WriteLine("{0,-20} {1,-40} version={2,-6} sourceUSN={3,-8} localUSN={4,-8} dsa={5,-16} value={6}", DateTime.Now, attribute, version, sourceUSN, localUSN, dsa, value);



            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
        }

        static void OpenLogFile()
        {
            if (!SaveLogFile) return;

            try
            {
                LogFileHandle = new System.IO.StreamWriter("GetDirSyncChanges.log", true);
            }
            catch
            {
                LogFileHandle = new System.IO.StreamWriter($"GetDirSyncChanges-{(DateTime.Now).ToString().Replace("-", "").Replace(":", "")}.log", true);
            }
        }

        static void CloseLogFile()
        {
            if (!SaveLogFile) return;
            LogFileHandle.Close();
        }

        static void FlushLogFile()
        {
            if (!SaveLogFile) return;
            LogFileHandle.Flush();
        }

        static string LongToDate(string myLong)
        {
            // LargeInteger could be a datetime value, so we'll try to convert to a datetime string

            string attributeValue = "";

            long asLongValue = Int64.Parse((myLong));

            // greater that 01/01/1999 which is 125596224000000000, but less than 01/01/2040 138534624000000000
            if (asLongValue > 125596224000000000 && asLongValue < 138534624000000000)
            {
                try
                {
                    DateTime dt = DateTime.FromFileTime(asLongValue);
                    attributeValue = dt.ToString();
                }
                catch
                {
                    attributeValue = asLongValue.ToString();
                }

                return attributeValue;
            }

            if (asLongValue == 0)
            {
                return "0";
            }

            if (asLongValue == 9223372036854775807)
            {
                return "never";
            }


            return asLongValue.ToString();
        }

        static string ReadPasswordMasked()
        {
            var pass = string.Empty;
            ConsoleKey key;
            do
            {
                var keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && pass.Length > 0)
                {
                    Console.Write("\b \b");
                    pass = pass.Substring(0, pass.Length - 1);
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    Console.Write("*");
                    pass += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);

            return pass;
        }

    }
}
