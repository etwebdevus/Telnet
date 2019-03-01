Imports System.Threading
Imports System.IO
Imports System.IO.Pipes

Public Class TelnetClient
    Private Server As String
    Private NetWorkProtocolClient As System.Net.Sockets.TcpClient
    Private ServerStream As System.Net.Sockets.NetworkStream
    Private DoReader As Boolean
    Private ReaderThread As Thread
    Private OutputPipe As AnonymousPipeServerStream
    Private WaitForString As String
    Private WaitForStringEvent As New AutoResetEvent(False)
    Public Response As String
    Public IsFinishedReading As Boolean
    Public mainFrm As frmMain
    Public Callback As Action(Of String)

    ReadOnly Property IsConnected() As Boolean
        Get
            Return (Not (IsNothing(NetWorkProtocolClient)) AndAlso (NetWorkProtocolClient.Connected))
        End Get
    End Property

    ReadOnly Property ConnectedTo() As String
        Get
            If (Not (IsNothing(NetWorkProtocolClient)) AndAlso (NetWorkProtocolClient.Connected)) Then
                Return NetWorkProtocolClient.Client.RemoteEndPoint.ToString()
            Else
                Return "Nothing"
            End If
        End Get
    End Property

    'Set the Server string to connect to.
    Public Sub SetServer(ByVal new_server As String)
        'double check this later
        Server = new_server
    End Sub
    'Connects if possilbe. If already conneced to some thing it Disconnects from old Telnet and connects to new Telnet.
    Public Sub Connect()
        IsFinishedReading = False
        Try
            If (Not (IsNothing(NetWorkProtocolClient))) AndAlso NetWorkProtocolClient.Connected Then
                Disconnect()
            End If
            If Not IsNothing(Server) Then
                NetWorkProtocolClient = New System.Net.Sockets.TcpClient(Server, 23)
                If NetWorkProtocolClient.Connected Then
                    'clear on a new client
                    WaitForString = Nothing
                    WaitForStringEvent.Reset()

                    NetWorkProtocolClient.NoDelay = True
                    ServerStream = NetWorkProtocolClient.GetStream()
                    ServerStream.ReadTimeout = 1000
                    DoReader = True
                    ReaderThread = New Thread(AddressOf ReaderTask)
                    ReaderThread.IsBackground = True
                    ReaderThread.Priority = ThreadPriority.AboveNormal
                    ReaderThread.Start()
                End If
            End If
        Catch ex As System.Net.Sockets.SocketException
            Console.WriteLine("SocketException Connect: {0}", ex)
        End Try
    End Sub

    'Disconnects if connected, otherwise does nothing.
    Public Sub Disconnect()
        Try
            If ReaderThread.IsAlive Then
                DoReader = False
                ReaderThread.Join(1000)
            End If
            If (Not (IsNothing(NetWorkProtocolClient))) Then
                ServerStream.Close()
                NetWorkProtocolClient.Close()
            End If
        Catch ex As System.Net.Sockets.SocketException
            Console.WriteLine("SocketException Disconnect: {0}", ex)
        End Try
    End Sub

    'Returns true if found before timeout milliseconds. Use -1 to have infinite wait time.
    'Returns false if timeout occured.
    Public Function WaitFor(ByVal command As String, ByVal timeout As Integer) As Boolean
        WaitForString = New String(command)
        WaitForStringEvent.Reset()
        Dim was_signaled As Boolean = False
        'Block until a the right value from reader or user defined timeout
        was_signaled = WaitForStringEvent.WaitOne(timeout)
        WaitForString = Nothing
        Return was_signaled
    End Function

    Public Sub Write(ByVal command As String)
        IsFinishedReading = False
        Try
            If (Not (IsNothing(NetWorkProtocolClient))) Then
                If NetWorkProtocolClient.Connected Then
                    'Write the value to the Stream
                    Dim bytes() As Byte = System.Text.Encoding.ASCII.GetBytes(command)
                    SyncLock ServerStream
                        ServerStream.Write(bytes, 0, bytes.Length)
                    End SyncLock
                End If
            End If
        Catch ex As System.Net.Sockets.SocketException
            Console.WriteLine("SocketException Write: {0}", ex)
        End Try
    End Sub

    'appends CrLf for the caller
    Public Sub WriteLine(ByVal command As String)
        IsFinishedReading = False
        Try
            If (Not (IsNothing(NetWorkProtocolClient))) Then
                If NetWorkProtocolClient.Connected Then
                    'Write the value to the Stream
                    Dim bytes() As Byte = System.Text.Encoding.ASCII.GetBytes(command & vbCrLf)
                    SyncLock ServerStream
                        ServerStream.Write(bytes, 0, bytes.Length)
                    End SyncLock
                End If
            End If
        Catch ex As System.Net.Sockets.SocketException
            Console.WriteLine("SocketException Write: {0}", ex)
        End Try
    End Sub

    'Get a pipe to read output. Note: anything written by WriteLine may be echoed back if the other Telnet offers to do it.
    Public Function GetPipeHandle() As String
        If Not IsNothing(ReaderThread) AndAlso ReaderThread.IsAlive AndAlso Not IsNothing(OutputPipe) Then
            Return OutputPipe.GetClientHandleAsString
        Else
            Return Nothing
        End If
    End Function

    'Task that watches the tcp stream, passes info to the negotiation function and signals the WaitFor task.
    Private Sub ReaderTask()
        Try
            OutputPipe = New AnonymousPipeServerStream(PipeDirection.Out)
            Dim prevData As New String("")
            While (DoReader)
                If (Not (IsNothing(NetWorkProtocolClient))) Then

                    If ServerStream.DataAvailable Then

                        'Grab Data
                        Dim data As [Byte]() = New [Byte](NetWorkProtocolClient.ReceiveBufferSize) {}
                        Dim bytes As Integer = ServerStream.Read(data, 0, data.Length)

                        'Negotiate anything that came in
                        bytes = Negotiate(data, bytes)

                        If (bytes > 0) Then
                            'append previous to the search sting incase messages were fragmented
                            Dim s As New String(prevData & System.Text.ASCIIEncoding.ASCII.GetChars(data))

                            'If Pipe is connected send it remaining real data
                            If OutputPipe.IsConnected Then
                                OutputPipe.Write(data, 0, bytes)
                                Response = System.Text.ASCIIEncoding.ASCII.GetChars(data)
                                Callback.Invoke(Response)
                            End If

                            'Check remaining against WaitForString
                            If Not IsNothing(WaitForString) Then
                                If s.Contains(WaitForString) Then
                                    WaitForStringEvent.Set()
                                    'clear prevData buffer because the WaitForString was found
                                    prevData = New String("")
                                Else
                                    'Nothing found make the current string part of the next string.
                                    prevData = New String(s)
                                End If
                            Else
                                prevData = New String("")
                            End If
                        End If

                    Else
                        Response = ""
                        IsFinishedReading = True
                        Thread.Sleep(100)
                    End If
                End If
            End While
            OutputPipe.Close()
            OutputPipe.Dispose()
        Catch ex As System.IO.IOException
            Console.WriteLine("IO Error: {0}", ex)
        Catch ex As System.Net.Sockets.SocketException
            Console.WriteLine("SocketException Reader: {0}", ex)
        End Try
    End Sub

    'Shamelessly adapted from http://www.codeproject.com/Articles/63201/TelnetSocket
    'The basic algorithm used here is:
    ' Iterate across the incoming bytes
    ' Assume that an IAC (byte 255) is the first of a two- or three-byte Telnet command and handle it:
    '   If two IACs are together, they represent one data byte 255
    '   Ignore the Go-Ahead command
    '   Respond WONT to all DOs and DONTs
    '   Respond DONT to all WONTs
    '   Respond DO to WILL ECHO and WILL SUPPRESS GO-AHEAD
    '   Respond DONT to all other WILLs
    ' Any other bytes are data; ignore nulls, and shift the rest as necessary
    ' Return the number of bytes that remain after removing the Telnet command and ignoring nulls
    Private Function Negotiate(ByVal data As Byte(), ByVal length As Int32) As Int32
        Dim index As Int32 = 0
        Dim remaining As Int32 = 0

        While (index < length)
            If (data(index) = TelnetBytes.IAC) Then
                Try
                    Select Case data(index + 1)
                        Case TelnetBytes.IAC
                            data(remaining) = data(index)
                            remaining += 1
                            index += 2

                        Case TelnetBytes.GA
                            index += 2

                        Case TelnetBytes.WDO
                            data(index + 1) = TelnetBytes.WONT
                            SyncLock ServerStream
                                ServerStream.Write(data, index, 3)
                            End SyncLock
                            index += 3

                        Case TelnetBytes.DONT
                            data(index + 1) = TelnetBytes.WONT
                            SyncLock ServerStream
                                ServerStream.Write(data, index, 3)
                            End SyncLock
                            index += 3

                        Case TelnetBytes.WONT
                            data(index + 1) = TelnetBytes.DONT
                            SyncLock ServerStream
                                ServerStream.Write(data, index, 3)
                            End SyncLock
                            index += 3

                        Case TelnetBytes.WILL
                            Dim action As Byte = TelnetBytes.DONT

                            Select Case data(index + 2)

                                Case TelnetBytes.ECHO
                                    action = TelnetBytes.WDO

                                Case TelnetBytes.SUPP
                                    action = TelnetBytes.WDO

                            End Select
                            data(index + 1) = action
                            SyncLock ServerStream
                                ServerStream.Write(data, index, 3)
                            End SyncLock
                            index += 3

                    End Select

                Catch ex As System.IndexOutOfRangeException
                    index = length
                End Try
            Else
                If (data(index) <> 0) Then
                    data(remaining) = data(index)
                    remaining += 1
                End If
                index += 1
            End If
        End While

        Return remaining
    End Function

    Private Structure TelnetBytes

        'Commands
        Public Const GA As Byte = 249
        Public Const WILL As Byte = 251
        Public Const WONT As Byte = 252
        Public Const WDO As Byte = 253 'Actually just DO but is protected word in vb.net 
        Public Const DONT As Byte = 254
        Public Const IAC As Byte = 255

        'Options
        Public Const ECHO As Byte = 1
        Public Const SUPP As Byte = 3
    End Structure
End Class