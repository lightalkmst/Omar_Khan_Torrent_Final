// General
using System;
// Arrays
using System.Collections.Generic;
// Standard Input/Output and File Input/Output
using System.IO;
// BEncoding library needs this
// I never use it
using System.Linq;
// BEncoding library needs this
// I use it for Regex
using System.Text;
using System.Text.RegularExpressions;
// Sleep()
using System.Threading;
// Networking
using System.Net;
using System.Net.Sockets;
// SHA1 hash
using System.Security.Cryptography;

namespace Torrent_Final {
    class Program {
        #region GlobalVariables
        #region Untouchable
        // These may not be changed
        const int MAX_BUFFER = 1024 * 1024;
        const int MAX_HANDSHAKE = (1 << 7) + 49;
        const string PSTR = @"BitTorrent protocol";
        #endregion

        #region Touchable
        // These may be changed
        const int MAX_QUEUE = 5;
        const int PEER_TTL = 2 * 60 * 1000;
        const int NUM_WANT = 10;
        const int FAIL_WAIT = 2000;
        const int MAIN_WAIT = 100;
        const int IN_PORT = 6888;
        #endregion
        #endregion

        static int Main(string[] args) {
            #region Init
            string host = Dns.GetHostName();
            IPHostEntry ip = Dns.GetHostEntry(host);
            for (int i = 0; i < ip.AddressList.Length; i++) {
                Console.WriteLine(ip.AddressList[i].ToString());
            }
            Console.WriteLine();
            string myip = ip.AddressList[0].ToString();
            Encoding ibm437 = Encoding.GetEncoding(437);
            #endregion

            #region CommandLine
            // Check for command line argument then find designated file
            // Read from standard input if command line argument does not exist
            string path = "";
            if (args.Length == 0) {
                Console.Write("No torrent file selected\n");
                Console.Write("Input file name: ");
                path = Directory.GetCurrentDirectory() + @"\" + Console.ReadLine().Trim();
                Console.WriteLine(path);
                Console.WriteLine();
            }
            else {
                path = Directory.GetCurrentDirectory() + @"\" + args[0];
            }
            #endregion
            
            #region ReadFile
            // Read file
            if (!File.Exists(path)) {
                Console.Write("Torrent file does not exist\n");
                System.Threading.Thread.Sleep(FAIL_WAIT);
                return 1;
            }
            string payload = "";
            Bencoding.BElement[] tfile = new Bencoding.BElement[0];
            StreamReader read = new StreamReader(path, ibm437);
            payload = read.ReadToEnd();
            tfile = Bencoding.BencodeDecoder.Decode(payload);
            #endregion

            #region TrackerStarter
            // Parse file
            // Get the primary tracker URL
            string trackerurl = ((Bencoding.BDictionary) (tfile[0]))["announce"].ToString();
            
            // Build the HTTP GET request parameters
            string suffix = "";
            suffix += @"?info_hash=";
            SHA1 infohash = new SHA1Managed();
            string infostring = ((Bencoding.BDictionary) (tfile[0]))["info"].ToBencodedString();
            infohash.ComputeHash(ibm437.GetBytes(infostring));
            suffix += Byte2Hashstring(infohash.Hash);
            suffix += @"&peer_id=";
            string myid = "0F0F0F0F0F0F0F0F0F0F";
            suffix += myid;
            suffix += @"&port=";
            suffix += IN_PORT;
            suffix += @"&uploaded=";
            long uploaded = 0;
            suffix += uploaded;
            suffix += @"&downloaded=";
            long downloaded = 0;
            suffix += downloaded;
            suffix += @"&left=";
            long left = Int64.Parse(((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["length"].ToBencodedString().Substring(1, ((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["length"].ToBencodedString().Length - 2));
            suffix += left;
            suffix += @"&event=started";
            suffix += @"&numwant=";
            suffix += NUM_WANT;
            suffix += @"&compact=1";
            suffix += @"&no_peer_id=1";

            // Change URL to HTTP prefix
            trackerurl = @"http://" + trackerurl.Substring(trackerurl.IndexOf(@"://") + 3);

            // Query the primary tracker
            string text = null;
            WebClient client = new WebClient();
            try {
                Console.WriteLine(trackerurl);
                text = client.DownloadString(trackerurl + suffix);
            }
            catch (Exception e) {
                // If connection to tracker failed
                Console.WriteLine(e.GetBaseException().Message);
                client.Dispose();
            }
            Bencoding.BElement[] response = new Bencoding.BElement[0];
            bool failed = true;
            // Check if response is actually a failure
            if (text != null) {
                response = Bencoding.BencodeDecoder.Decode(text);
                if (response[0].ToBencodedString().IndexOf("failure reason") < 0) {
                    failed = false;
                }
            }
            // If there was a failure
            try {
                // Try each alternate tracker
                for (int i = 0; failed && ((Bencoding.BList) ((Bencoding.BDictionary) (tfile[0]))["announce-list"])[i] != null; i++) {
                    string temp = ((Bencoding.BList) ((Bencoding.BDictionary) (tfile[0]))["announce-list"])[i].ToBencodedString();
                    text = null;
                    // Change prefix to HTTP
                    if (temp.IndexOf("http://") < 0)
                        continue;
                    trackerurl = @"http://" + temp.Substring(temp.IndexOf("://") + 3, temp.Length - 1 - temp.IndexOf("://") - 3);
                    client = new WebClient();
                    try {
                        Console.WriteLine();
                        Console.WriteLine(trackerurl);
                        text = client.DownloadString(trackerurl + suffix);
                    }
                    catch (Exception e) {
                        // If connection to tracker failed
                        Console.WriteLine(e.GetBaseException().Message);
                        client.Dispose();
                    }
                    response = new Bencoding.BElement[0];
                    failed = true;
                    // Check if response is actually a failure
                    if (text != null) {
                        response = Bencoding.BencodeDecoder.Decode(text);
                        if (response[0].ToBencodedString().IndexOf("failure reason") < 0) {
                            failed = false;
                        }
                    }
                }
            }
            catch (Exception) {
            }

            // Check if all tracker connection attempts were failures
            Console.WriteLine();
            if (!failed) {
                Console.WriteLine("Successfully connected to a tracker");
            }
            else {
                Console.WriteLine("Could not connect to any trackers");
                Thread.Sleep(FAIL_WAIT);
                return 1;
            }
            #endregion

            #region MainLoop
            // Parsing the tracker response
            long interval = ((Bencoding.BInteger) ((Bencoding.BDictionary) (response[0]))["interval"]).Value;
            long timetoupdate = interval / 10 * 9;
            // Prime listener
            TcpListener inb = new TcpListener(IPAddress.Any, IN_PORT);
            inb.Server.Blocking = false;
            inb.Start(MAX_QUEUE);
            TcpClient[] peers = new TcpClient[0];
            int[] peerttl = new int[0];
            int[] am_choking = new int[0];
            int[] am_interested = new int[0];
            int[] peer_choking = new int[0];
            long filelength = ((Bencoding.BInteger) ((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["length"]).Value;
            long piecelength = ((Bencoding.BInteger) ((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["piece length"]).Value;
            long numpieces = ((Bencoding.BString) ((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["pieces"]).Value.Length / 20;
            string filename = ((Bencoding.BString) ((Bencoding.BDictionary) ((Bencoding.BDictionary) (tfile[0]))["info"])["name"]).Value;
            int[] peer_interested = new int[0];
            bool[][] peer_pieces = new bool[0][];
            bool[] have_pieces = new bool[numpieces + numpieces % 8];
            // Main loop
            Console.Write("Starting Main Loop\n");
            for (; ; ) {
                #region TrackerQuery
                // Periodically request and parse HTTP information
                if (timetoupdate <= 0) {
                    Console.WriteLine("-----------------------------------------------");
                    #region QueryTracker
                    // Request tracker information
                    suffix = "";
                    suffix += @"?info_hash=";
                    suffix += Byte2Hashstring(infohash.Hash);
                    suffix += @"&peer_id=";
                    suffix += myid;
                    suffix += @"&port=";
                    suffix += IN_PORT;
                    suffix += @"&uploaded=";
                    suffix += uploaded;
                    suffix += @"&downloaded=";
                    suffix += downloaded;
                    suffix += @"&left=";
                    suffix += left;
                    suffix += @"&numwant=";
                    suffix += NUM_WANT;
                    suffix += @"&compact=1";
                    suffix += @"&no_peer_id=1";
                    try {
                        text = client.DownloadString(trackerurl + suffix);
                    }
                    catch (Exception e) {
                        // If connection to tracker failed
                        Console.WriteLine(e.GetBaseException().Message);
                        client.Dispose();
                    }
                    response = new Bencoding.BElement[0];
                    failed = true;
                    // Check if response is actually a failure
                    if (text != null) {
                        response = Bencoding.BencodeDecoder.Decode(text);
                        if (response[0].ToBencodedString().IndexOf("failure reason") < 0) {
                            failed = false;
                        }
                    }
                    if (failed) {
                        Console.WriteLine("Connection with tracker broken");
                        Thread.Sleep(FAIL_WAIT);
                        return 1;
                    }
                    #endregion

                    interval = ((Bencoding.BInteger) ((Bencoding.BDictionary) (response[0]))["interval"]).Value;
                    timetoupdate = interval;

                    #region ConnectToPeers
                    // Parse tracker information
                    string peerlist = ((Bencoding.BString) ((Bencoding.BDictionary) (response[0]))["peers"]).ToBencodedString();
                    peerlist = peerlist.Substring(peerlist.IndexOf(":") + 1);
                    int peerlen = peerlist.Length;
                    for (int i = 0; i < peerlist.Length; i += 6) {
                        // Convert response from byte data
                        byte[] ipbytes = GetBytes(peerlist.Substring(i, 4));
                        byte[] portbytes = GetBytes(peerlist.Substring(i + 4, 2));
                        string ipstring = ipbytes[0] + "." + ipbytes[2] + "." + ipbytes[4] + "." + ipbytes[6];
                        int portint = portbytes[0] * (byte.MaxValue + 1) + portbytes[2];
                        // Sometimes I get weird data in between bytes
                        // Actual IP and port number differ from what I calculate in these cases
                        // I don't see a correlation between the noise and error so I drop them
                        if (ipbytes[1] != 0 || ipbytes[3] != 0 || ipbytes[5] != 0 || ipbytes[7] != 0 || portbytes[1] != 0 || portbytes[3] != 0) {
                            Console.WriteLine("\tSkip!");
                            continue;
                        }
                        Console.WriteLine("Outbound: {0}:{1}", ipstring, portint);
                        // Attempt connection to peer
                        try {
                            TcpClient temppeer = new TcpClient(ipstring, portint);
                            NetworkStream stream = temppeer.GetStream();
                            // Immediately send the handshake (Currently byte-wise representation of the data)
                            byte[] handshake = new byte[MAX_HANDSHAKE];
                            // Pstrlen
                            handshake[0] = (byte) PSTR.Length;
                            // Pstr
                            for (int i2 = 0; i2 < PSTR.ToCharArray().Length; i2++) {
                                handshake[i2 + 1] = (byte) PSTR.ToCharArray()[i2];
                            }
                            // Reserved
                            // 8 bytes left as 0
                            for (int i2 = 0; i2 < infohash.Hash.Length; i2++) {
                                handshake[1 + PSTR.Length + 8 + i2] = infohash.Hash[i2];
                            }
                            // Peer ID
                            for (int i2 = 0; i2 < myid.Length; i2++) {
                                handshake[1 + PSTR.Length + 8 + infohash.Hash.Length + i2] = (byte) myid.ToCharArray()[i2];
                            }
                            stream.Write(handshake, 0, 49 + PSTR.Length);
                            //stream.Read(handshake, 0, handshake.Length);
                            stream.Read(handshake, 0, 1);
                            stream.Read(handshake, 1, 49 - 1 + handshake[0]);
                            // Verify correct info_hash
                            bool pass = true;
                            for (int i2 = 0; i2 < infohash.Hash.Length; i2++) {
                                if (infohash.Hash[i2] != handshake[1 + handshake[0] + 8 + i2]) {
                                    pass = false;
                                    break;
                                }
                            }
                            if (!pass) {
                                Console.WriteLine("\tDenied: Peer did not have matching info_hash");
                                temppeer.Close();
                                continue;
                            }
                            // Add peer to active peer list
                            peers = TcpAdd(peers, temppeer);
                            peerttl = IntAdd(peerttl, PEER_TTL);
                            am_choking = IntAdd(am_choking, 1);
                            am_interested = IntAdd(am_interested, 0);
                            peer_choking = IntAdd(peer_choking, 1);
                            peer_interested = IntAdd(peer_interested, 0);
                            peer_pieces = BoolAdd(peer_pieces, new bool[numpieces + numpieces % 8]);
                            Console.WriteLine("\tSuccess");
                        }
                        catch (Exception e) {
                            // Connection failed
                            Console.Write("\tFailure: ");
                            Console.WriteLine(e.GetBaseException().Message);
                        }
                    }
                    #endregion
                }
                #endregion

                #region AcceptConnections
                // Accept incoming connections from peers
                while (inb.Pending()) {
                    TcpClient temppeer;
                    while ((temppeer = inb.AcceptTcpClient()) != null) {
                        string ipstring = IPAddress.Parse(((IPEndPoint) temppeer.Client.RemoteEndPoint).Address.ToString()).ToString();
                        int portint = ((IPEndPoint) temppeer.Client.RemoteEndPoint).Port;
                        Console.WriteLine("Inbound: {0}:{1}", ipstring, portint);
                        // Immediately send the handshake (Currently byte-wise representation of the data)
                        NetworkStream stream = temppeer.GetStream();
                        byte[] handshake = new byte[MAX_HANDSHAKE];
                        stream.Read(handshake, 0, handshake.Length);
                        // Verify correct info_hash
                        bool pass = true;
                        for (int i2 = 0; i2 < infohash.Hash.Length; i2++) {
                            if (infohash.Hash[i2] != handshake[1 + handshake[0] + 8 + i2]) {
                                pass = false;
                                break;
                            }
                        }
                        if (!pass) {
                            Console.WriteLine("\tDenied: Peer did not have matching info_hash");
                            temppeer.Close();
                            continue;
                        }
                        // Return handshake
                        // Pstrlen
                        handshake[0] = (byte) PSTR.Length;
                        // Pstr
                        for (int i2 = 0; i2 < PSTR.ToCharArray().Length; i2++) {
                            handshake[i2 + 1] = (byte) PSTR.ToCharArray()[i2];
                        }
                        // Reserved
                        // 8 bytes left as 0
                        for (int i2 = 0; i2 < infohash.Hash.Length; i2++) {
                            handshake[1 + PSTR.Length + 8 + i2] = infohash.Hash[i2];
                        }
                        // Peer ID
                        for (int i2 = 0; i2 < myid.Length; i2++) {
                            handshake[1 + PSTR.Length + 8 + infohash.Hash.Length + i2] = (byte) myid.ToCharArray()[i2];
                        }
                        stream.Write(handshake, 0, handshake.Length);
                        peers = TcpAdd(peers, temppeer);
                        peerttl = IntAdd(peerttl, PEER_TTL);
                        am_choking = IntAdd(am_choking, 1);
                        am_interested = IntAdd(am_interested, 0);
                        peer_choking = IntAdd(peer_choking, 1);
                        peer_interested = IntAdd(peer_interested, 0);
                        peer_pieces = BoolAdd(peer_pieces, new bool[numpieces + numpieces % 8]);
                        Console.WriteLine("Connection from peer accepted");
                    }
                }
                #endregion

                #region HandleConnections
                // Handle connections with peers
                for (int i = 0; i < peers.Length; i++) {
                    string ipstring = IPAddress.Parse(((IPEndPoint) peers[i].Client.RemoteEndPoint).Address.ToString()).ToString();
                    int portint = ((IPEndPoint) peers[i].Client.RemoteEndPoint).Port;
                    Console.WriteLine("Polling: {0}:{1}", ipstring, portint);
                    if (peers[i].GetStream().DataAvailable) {
                        peerttl[i] = PEER_TTL;
                        NetworkStream stream = peers[i].GetStream();
                        byte[] buffer = new byte[MAX_BUFFER];
                        stream.Read(buffer, 0, 4);
                        int len = buffer[0] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[1] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[2] * (byte.MaxValue + 1) + buffer[3] - 1;
                        if (len == 0) {
                            continue;
                        }
                        stream.Read(buffer, 4, 1);
                        int id = buffer[4];
                        if (id == 0) {
                            peer_choking[i] = 1;
                            Console.WriteLine("Choking: {0}:{1}", ipstring, portint);
                            continue;
                        }
                        if (id == 1) {
                            peer_choking[i] = 0;
                            Console.WriteLine("Unchoking: {0}:{1}", ipstring, portint);
                            continue;
                        }
                        if (id == 2) {
                            peer_interested[i] = 1;
                            Console.WriteLine("Interested: {0}:{1}", ipstring, portint);
                            continue;
                        }
                        if (id == 3) {
                            peer_interested[i] = 0;
                            Console.WriteLine("Uninterested: {0}:{1}", ipstring, portint);
                            continue;
                        }
                        if (id == 4) {
                            stream.Read(buffer, 0, 4);
                            int length = buffer[0] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[1] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[2] * (byte.MaxValue + 1) + buffer[3];
                            Console.WriteLine("Have: {0}:{1} {2}", ipstring, portint, length);
                            continue;
                        }
                        if (id == 5) {
                            stream.Read(buffer, 0, len);
                            peer_pieces[i] = new bool[numpieces + numpieces % 8];
                            for (int i2 = 0; i2 < len; i2++) {
                                try {
                                    peer_pieces[i][i2 * 8 + 0] = (buffer[i2] >> 7) - (buffer[i2] >> 8 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 1] = (buffer[i2] >> 6) - (buffer[i2] >> 7 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 2] = (buffer[i2] >> 5) - (buffer[i2] >> 6 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 3] = (buffer[i2] >> 4) - (buffer[i2] >> 5 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 4] = (buffer[i2] >> 3) - (buffer[i2] >> 4 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 5] = (buffer[i2] >> 2) - (buffer[i2] >> 3 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 6] = (buffer[i2] >> 1) - (buffer[i2] >> 2 << 1) == 1;
                                    peer_pieces[i][i2 * 8 + 7] = (buffer[i2] >> 0) - (buffer[i2] >> 1 << 1) == 1;
                                }
                                catch (Exception) {
                                    break;
                                }
                            }
                            Console.WriteLine("Bitfield: {0}:{1}", ipstring, portint);
                            continue;
                        }
                        if (id == 6) {
                            stream.Read(buffer, 0, 12);
                            long index = buffer[0] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[1] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[2] * (byte.MaxValue + 1) + buffer[3];
                            long begin = buffer[4] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[5] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[6] * (byte.MaxValue + 1) + buffer[9];
                            long length = buffer[8] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[9] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[10] * (byte.MaxValue + 1) + buffer[11];
                            Console.WriteLine("Request: {0}:{1} {2},{3},{4}", ipstring, portint, index, begin, length);
                            continue;
                        }
                        if (id == 7) {
                            stream.Read(buffer, 0, 8);
                            long index = buffer[0] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[1] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[2] * (byte.MaxValue + 1) + buffer[3];
                            long begin = buffer[4] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[5] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[6] * (byte.MaxValue + 1) + buffer[9];
                            long length = len - 8;
                            stream.Read(buffer, 0, len - 8);
                            // Create a file and write to it
                            string tempname = filename + "." + index + "." + length;
                            if (!File.Exists(tempname)) {
                                File.Create(tempname);
                                File.WriteAllBytes(tempname, buffer);
                            }
                            // Get all files for the same piece in directory
                            List<string> filelist = new List<string>();
                            foreach (string s in Directory.GetFiles("", "*")) {
                                filelist.Add(s);
                            }
                            int furthest = 0;
                            int curpos = 0;
                            Regex regex = new Regex("*." + index + ".[0-9]*.[0-9]*");
                            // Check if they can be combined to create full piece
                            for (int i2 = 0; i2 < filelist.Count; i2++) {
                                for (int i3 = 0; i3 < filelist.Count; i3++) {
                                    string cur = filelist.ElementAt(i3);
                                    if (!regex.IsMatch(cur))
                                        continue;
                                    string cur2 = cur.Substring(cur.LastIndexOf('.'));
                                    string cur3 = cur.Substring(0, cur.Length - cur2.Length);
                                    string cur4 = cur3.Substring(cur3.LastIndexOf('.'));
                                    /*
                                    if (Int64.Parse(cur2.Substring(1)) ) {

                                    }
                                     * */
                                }
                            }

                            Console.WriteLine("Piece: {0}:{1} {2},{3}", ipstring, portint, index, begin);
                            continue;
                        }
                        if (id == 8) {
                            stream.Read(buffer, 0, 12);
                            long index = buffer[0] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[1] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[2] * (byte.MaxValue + 1) + buffer[3];
                            long begin = buffer[4] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[5] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[6] * (byte.MaxValue + 1) + buffer[9];
                            long length = buffer[8] * (byte.MaxValue + 1) * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[9] * (byte.MaxValue + 1) * (byte.MaxValue + 1) + buffer[10] * (byte.MaxValue + 1) + buffer[11];
                            Console.WriteLine("Cancel: {0}:{1} {2},{3},{4}", ipstring, portint, index, begin, length);
                            continue;
                        }
                    }
                    else if (peerttl[i] < 0) {
                        peers[i].Close();
                        peers = TcpRemove(peers, i);
                        peerttl = IntRemove(peerttl, i);
                        am_choking = IntRemove(am_choking, i);
                        am_interested = IntRemove(am_interested, i);
                        peer_choking = IntRemove(peer_choking, i);
                        peer_interested = IntRemove(peer_interested, i);
                        i--;
                    }
                }
                #endregion

                //for peers that are not choked
                //   request pieces from outcoming traffic

                //check liveliness of peers and replace dead (or useless) peers
                //with new potentially useful peers

                //update peers

                #region Completion
                bool complete = true;
                for (int i = 0; i < numpieces; i++) {
                    if (!have_pieces[i]) {
                        complete = false;
                        break;
                    }
                }
                if (complete) {
                    break;
                }
                #endregion

                #region WaitAndTimeUpdate
                Console.WriteLine("  -  -  -  -  -  -  -  -  -  -  -  -  -  -  -  ");
                System.Threading.Thread.Sleep(MAIN_WAIT);
                timetoupdate -= MAIN_WAIT;
                for (int i = 0; i < peerttl.Length; i++) {
                    peerttl[i] -= MAIN_WAIT;
                }
                #endregion
            }
            #endregion

            return 0;
        }

        static TcpClient[] TcpAdd(TcpClient[] arr, TcpClient x) {
            TcpClient[] temp = new TcpClient[arr.Length + 1];
            for (int i = 0; i < arr.Length; i++) {
                temp[i] = arr[i];
            }
            temp[arr.Length] = x;
            return temp;
        }

        static TcpClient[] TcpRemove(TcpClient[] arr, int x) {
            TcpClient[] temp = new TcpClient[arr.Length - 1];
            int i = 0;
            for (; i < x; i++) {
                temp[i] = arr[i];
            }
            for (; i < temp.Length; i++) {
                temp[i] = arr[i + 1];
            }
            return temp;
        }

        static int[] IntAdd(int[] arr, int x) {
            int[] temp = new int[arr.Length + 1];
            for (int i = 0; i < arr.Length; i++) {
                temp[i] = arr[i];
            }
            temp[arr.Length] = x;
            return temp;
        }

        static int[] IntRemove(int[] arr, int x) {
            int[] temp = new int[arr.Length - 1];
            int i = 0;
            for (; i < x; i++) {
                temp[i] = arr[i];
            }
            for (; i < temp.Length; i++) {
                temp[i] = arr[i + 1];
            }
            return temp;
        }

        static bool[][] BoolAdd(bool[][] arr, bool[] x) {
            bool[][] temp = new bool[arr.Length + 1][];
            for (int i = 0; i < arr.Length; i++) {
                temp[i] = arr[i];
            }
            temp[arr.Length] = x;
            return temp;
        }

        static bool[][] BoolRemove(bool[][] arr, int x) {
            bool[][] temp = new bool[arr.Length - 1][];
            int i = 0;
            for (; i < x; i++) {
                temp[i] = arr[i];
            }
            for (; i < temp.Length; i++) {
                temp[i] = arr[i + 1];
            }
            return temp;
        }

        static string URLEncode(string str) {
            return Uri.EscapeDataString(str).Replace("%20", "+");
        }

        static string Byte2Hashstring(byte[] arr) {
            string temp1 = "";
            string temp2 = BitConverter.ToString(arr).Replace("-", "");
            for (int i = 0; i < temp2.Length / 2; i++) {
                temp1 += @"%" + temp2.Substring(i * 2, 2);
            }
            return temp1;
        }

        static byte[] GetBytes(string str) {
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}

/*
 * My code ends here.
 * Past this point is an imported bencoding library.
 * I didn't feel like doing an actual file import so I pasted it here.
 * ********************************************************************************************************
 * ********************************************************************************************************
 * ********************************************************************************************************
 * ********************************************************************************************************
 * ********************************************************************************************************
 * */

#region BEncode
/*****
     * Encoding usage:
     * 
     * new BDictionary()
     * {
     *  {"Some Key", "Some Value"},
     *  {"Another Key", 42}
     * }.ToBencodedString();
     * 
     * Decoding usage:
     * 
     * BencodeDecoder.Decode("d8:Some Key10:Some Value13:Another Valuei42ee");
     * 
     * Feel free to use it.
     * More info about Bencoding at http://wiki.theory.org/BitTorrentSpecification#bencoding
     * 
     * Originally posted at http://snipplr.com/view/37790/ by SuprDewd.
     * */

namespace Bencoding {
    /// <summary>
    /// A class used for decoding Bencoding.
    /// </summary>
    public static class BencodeDecoder {
        /// <summary>
        /// Decodes the string.
        /// </summary>
        /// <param name="bencodedString">The bencoded string.</param>
        /// <returns>An array of root elements.</returns>
        public static BElement[] Decode(string bencodedString) {
            int index = 0;

            try {
                if (bencodedString == null) return null;

                List<BElement> rootElements = new List<BElement>();
                while (bencodedString.Length > index) {
                    rootElements.Add(ReadElement(ref bencodedString, ref index));
                }

                return rootElements.ToArray();
            }
            catch (BencodingException) { throw; }
            catch (Exception e) {
                Console.Write("Bencoding library error\n");
                throw Error(e);
            }
        }

        private static BElement ReadElement(ref string bencodedString, ref int index) {
            switch (bencodedString[index]) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9': return ReadString(ref bencodedString, ref index);
                case 'i': return ReadInteger(ref bencodedString, ref index);
                case 'l': return ReadList(ref bencodedString, ref index);
                case 'd': return ReadDictionary(ref bencodedString, ref index);
                default: throw Error();
            }
        }

        private static BDictionary ReadDictionary(ref string bencodedString, ref int index) {
            index++;
            BDictionary dict = new BDictionary();

            try {
                while (bencodedString[index] != 'e') {
                    BString K = ReadString(ref bencodedString, ref index);
                    BElement V = ReadElement(ref bencodedString, ref index);
                    dict.Add(K, V);
                }
            }
            catch (BencodingException) { throw; }
            catch (Exception e) { throw Error(e); }

            index++;
            return dict;
        }

        private static BList ReadList(ref string bencodedString, ref int index) {
            index++;
            BList lst = new BList();

            try {
                while (bencodedString[index] != 'e') {
                    lst.Add(ReadElement(ref bencodedString, ref index));
                }
            }
            catch (BencodingException) { throw; }
            catch (Exception e) { throw Error(e); }

            index++;
            return lst;
        }

        private static BInteger ReadInteger(ref string bencodedString, ref int index) {
            index++;

            int end = bencodedString.IndexOf('e', index);
            if (end == -1) throw Error();

            long integer;

            try {
                integer = Convert.ToInt64(bencodedString.Substring(index, end - index));
                index = end + 1;
            }
            catch (Exception e) { throw Error(e); }

            return new BInteger(integer);
        }

        private static BString ReadString(ref string bencodedString, ref int index) {
            int length, colon;

            try {
                colon = bencodedString.IndexOf(':', index);
                if (colon == -1) throw Error();
                length = Convert.ToInt32(bencodedString.Substring(index, colon - index));
            }
            catch (Exception e) { throw Error(e); }

            index = colon + 1;
            int tmpIndex = index;
            index += length;

            try {
                return new BString(bencodedString.Substring(tmpIndex, length));
            }
            catch (Exception e) { throw Error(e); }
        }

        private static Exception Error(Exception e) {
            return new BencodingException("Bencoded string invalid.", e);
        }

        private static Exception Error() {
            return new BencodingException("Bencoded string invalid.");
        }
    }

    /// <summary>
    /// An interface for bencoded elements.
    /// </summary>
    public interface BElement {
        /// <summary>
        /// Generates the bencoded equivalent of the element.
        /// </summary>
        /// <returns>The bencoded equivalent of the element.</returns>
        string ToBencodedString();

        /// <summary>
        /// Generates the bencoded equivalent of the element.
        /// </summary>
        /// <param name="u">The StringBuilder to append to.</param>
        /// <returns>The bencoded equivalent of the element.</returns>
        StringBuilder ToBencodedString(StringBuilder u);
    }

    /// <summary>
    /// A bencode integer.
    /// </summary>
    public class BInteger : BElement, IComparable<BInteger> {
        /// <summary>
        /// Allows you to set an integer to a BInteger.
        /// </summary>
        /// <param name="i">The integer.</param>
        /// <returns>The BInteger.</returns>
        public static implicit operator BInteger(int i) {
            return new BInteger(i);
        }

        /// <summary>
        /// The value of the bencoded integer.
        /// </summary>
        public long Value { get; set; }

        /// <summary>
        /// The main constructor.
        /// </summary>
        /// <param name="value">The value of the bencoded integer.</param>
        public BInteger(long value) {
            this.Value = value;
        }

        /// <summary>
        /// Generates the bencoded equivalent of the integer.
        /// </summary>
        /// <returns>The bencoded equivalent of the integer.</returns>
        public string ToBencodedString() {
            return this.ToBencodedString(new StringBuilder()).ToString();
        }

        /// <summary>
        /// Generates the bencoded equivalent of the integer.
        /// </summary>
        /// <returns>The bencoded equivalent of the integer.</returns>
        public StringBuilder ToBencodedString(StringBuilder u) {
            if (u == null) u = new StringBuilder('i');
            else u.Append('i');
            return u.Append(Value.ToString()).Append('e');
        }

        /// <see cref="Object.GetHashCode()"/>
        public override int GetHashCode() {
            return this.Value.GetHashCode();
        }

        /// <summary>
        /// Int32.Equals(object)
        /// </summary>
        public override bool Equals(object obj) {
            try {
                return this.Value.Equals(((BInteger) obj).Value);
            }
            catch { return false; }
        }

        /// <see cref="Object.ToString()"/>
        public override string ToString() {
            return this.Value.ToString();
        }

        /// <see cref="IComparable.CompareTo(object)"/>
        public int CompareTo(BInteger other) {
            return this.Value.CompareTo(other.Value);
        }
    }

    /// <summary>
    /// A bencode string.
    /// </summary>
    public class BString : BElement, IComparable<BString> {
        /// <summary>
        /// Allows you to set a string to a BString.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <returns>The BString.</returns>
        public static implicit operator BString(string s) {
            return new BString(s);
        }

        /// <summary>
        /// The value of the bencoded integer.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// The main constructor.
        /// </summary>
        /// <param name="value"></param>
        public BString(string value) {
            this.Value = value;
        }

        /// <summary>
        /// Generates the bencoded equivalent of the string.
        /// </summary>
        /// <returns>The bencoded equivalent of the string.</returns>
        public string ToBencodedString() {
            return this.ToBencodedString(new StringBuilder()).ToString();
        }

        /// <summary>
        /// Generates the bencoded equivalent of the string.
        /// </summary>
        /// <param name="u">The StringBuilder to append to.</param>
        /// <returns>The bencoded equivalent of the string.</returns>
        public StringBuilder ToBencodedString(StringBuilder u) {
            if (u == null) u = new StringBuilder(this.Value.Length);
            else u.Append(this.Value.Length);
            return u.Append(':').Append(this.Value);
        }

        /// <see cref="Object.GetHashCode()"/>
        public override int GetHashCode() {
            return this.Value.GetHashCode();
        }

        /// <summary>
        /// String.Equals(object)
        /// </summary>
        public override bool Equals(object obj) {
            try {
                return this.Value.Equals(((BString) obj).Value);
            }
            catch { return false; }
        }

        /// <see cref="Object.ToString()"/>
        public override string ToString() {
            return this.Value.ToString();
        }

        /// <see cref="IComparable.CompareTo(Object)"/>
        public int CompareTo(BString other) {
            return this.Value.CompareTo(other.Value);
        }
    }

    /// <summary>
    /// A bencode list.
    /// </summary>
    public class BList : List<BElement>, BElement {
        /// <summary>
        /// Generates the bencoded equivalent of the list.
        /// </summary>
        /// <returns>The bencoded equivalent of the list.</returns>
        public string ToBencodedString() {
            return this.ToBencodedString(new StringBuilder()).ToString();
        }

        /// <summary>
        /// Generates the bencoded equivalent of the list.
        /// </summary>
        /// <param name="u">The StringBuilder to append to.</param>
        /// <returns>The bencoded equivalent of the list.</returns>
        public StringBuilder ToBencodedString(StringBuilder u) {
            if (u == null) u = new StringBuilder('l');
            else u.Append('l');

            foreach (BElement element in base.ToArray()) {
                element.ToBencodedString(u);
            }

            return u.Append('e');
        }

        /// <summary>
        /// Adds the specified value to the list.
        /// </summary>
        /// <param name="value">The specified value.</param>
        public void Add(string value) {
            base.Add(new BString(value));
        }

        /// <summary>
        /// Adds the specified value to the list.
        /// </summary>
        /// <param name="value">The specified value.</param>
        public void Add(int value) {
            base.Add(new BInteger(value));
        }
    }

    /// <summary>
    /// A bencode dictionary.
    /// </summary>
    public class BDictionary : SortedDictionary<BString, BElement>, BElement {
        /// <summary>
        /// Generates the bencoded equivalent of the dictionary.
        /// </summary>
        /// <returns>The bencoded equivalent of the dictionary.</returns>
        public string ToBencodedString() {
            return this.ToBencodedString(new StringBuilder()).ToString();
        }

        /// <summary>
        /// Generates the bencoded equivalent of the dictionary.
        /// </summary>
        /// <param name="u">The StringBuilder to append to.</param>
        /// <returns>The bencoded equivalent of the dictionary.</returns>
        public StringBuilder ToBencodedString(StringBuilder u) {
            if (u == null) u = new StringBuilder('d');
            else u.Append('d');

            for (int i = 0; i < base.Count; i++) {
                base.Keys.ElementAt(i).ToBencodedString(u);
                base.Values.ElementAt(i).ToBencodedString(u);
            }

            return u.Append('e');
        }

        /// <summary>
        /// Adds the specified key-value pair to the dictionary.
        /// </summary>
        /// <param name="key">The specified key.</param>
        /// <param name="value">The specified value.</param>
        public void Add(string key, BElement value) {
            base.Add(new BString(key), value);
        }

        /// <summary>
        /// Adds the specified key-value pair to the dictionary.
        /// </summary>
        /// <param name="key">The specified key.</param>
        /// <param name="value">The specified value.</param>
        public void Add(string key, string value) {
            base.Add(new BString(key), new BString(value));
        }

        /// <summary>
        /// Adds the specified key-value pair to the dictionary.
        /// </summary>
        /// <param name="key">The specified key.</param>
        /// <param name="value">The specified value.</param>
        public void Add(string key, int value) {
            base.Add(new BString(key), new BInteger(value));
        }

        /// <summary>
        /// Gets or sets the value assosiated with the specified key.
        /// </summary>
        /// <param name="key">The key of the value to get or set.</param>
        /// <returns>The value assosiated with the specified key.</returns>
        public BElement this[string key] {
            get {
                return this[new BString(key)];
            }
            set {
                this[new BString(key)] = value;
            }
        }
    }

    /// <summary>
    /// A bencoding exception.
    /// </summary>
    public class BencodingException : FormatException {
        /// <summary>
        /// Creates a new BencodingException.
        /// </summary>
        public BencodingException() { }

        /// <summary>
        /// Creates a new BencodingException.
        /// </summary>
        /// <param name="message">The message.</param>
        public BencodingException(string message) : base(message) { }

        /// <summary>
        /// Creates a new BencodingException.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner exception.</param>
        public BencodingException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A class with extension methods for use with Bencoding.
    /// </summary>
    public static class BencodingExtensions {
        /// <summary>
        /// Decode the current instance.
        /// </summary>
        /// <param name="s">The current instance.</param>
        /// <returns>The root elements of the decoded string.</returns>
        public static BElement[] BDecode(this string s) {
            return BencodeDecoder.Decode(s);
        }
    }
}
#endregion