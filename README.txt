We were given two months for this assignment. I did it within a week. During the grading interview, the teacher's assistant told me a couple things: I was the only solo team, and I was the only one to get past connecting to the peers.

So far what I have is:

Takes 0 or 1 command line arguments
If there is a command line argument
	Take it as the name of the .torrent file
Else
	Read in the name of the .torrent file from standard input
If .torrent file does not exist
	Terminate
Reads in the .torrent file using 437 (IBM437) encoding
Un-bencode's the file

Queries the primary tracker
If query succeeds
	Stores response
Else
	Iterates through all alternate trackers
		Queries the alternate trackers
If all queries failed
	Terminate

Parses tracker response for initial iteration of main loop
Main loop
	If interval has passed
		Query tracker
		If query succeeds
			De-bencode response
			Attempt connection to peers in list
			If successful connection
				Send handshake
				Receive handshake
				If info_hash matches
					Add peer to active connections
				Else
					Close connection
		Else
			Terminate
	
	While there are any pending connections
		If a peer is requesting a connection
			(Haven't been able to test as I don't get requested connections)
			Receive handshake
			If info_hash matches
				Send handshake
				Add peer to active connections
			Else
				Close connection

	For each active connection
		If peer has sent a message
			Reset connection time to live
			Parse message
				ID 1-4
					Sets flag appropriately
				ID 5
					Logs which pieces are had
				ID 6
					(Currently mines data but does nothing)
				ID 7
					Downloads data to file named filename.index.length
					Checks if files can be consolidated into a single piece (Incomplete)
				ID 8
					(Currently mines data but does nothing)
		If connection time to live is over
			Close connection

	If have all pieces
		Break


Notes:
Only supports compact mode
