Imports Telnet
Imports System.IO
Imports System.Text
Public Class frmMain

    Public telnet As TelnetClient
    Private ResponseList As New List(Of String)

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        telnet = New TelnetClient
        telnet.mainFrm = Me
        telnet.Callback = AddressOf ReceiveData
    End Sub

    Public Sub ReceiveData(ByVal Response As String)
        ResponseList.Add(Response)
    End Sub

    Private Sub btnConnect_ClickAsync(sender As Object, e As EventArgs) Handles btnConnect.Click
        Dim sHost As String
        Dim sPort As String
        Dim sUser As String
        Dim sPassword As String

        sHost = tbHost.Text
        sPort = tbPort.Text
        sUser = tbUser.Text
        sPassword = tbPassword.Text

        If btnConnect.Text.Contains("Connect") Then
            telnet.SetServer(sHost)
            telnet.Connect()
            tbLog.Text = tbLog.Text + telnet.Response
            telnet.WriteLine(sUser)
            tbLog.Text = tbLog.Text + telnet.Response
            telnet.WriteLine(sPassword)
            tbLog.Text = tbLog.Text + telnet.Response
        Else
            telnet.Disconnect()
            btnConnect.Text = "Connect"
        End If
    End Sub

    Private Sub btnSend_ClickAsync(sender As Object, e As EventArgs) Handles btnSend.Click
        If telnet.IsConnected Then
            telnet.Response = ""
            telnet.WriteLine(tbCommand.Text)
            While Not telnet.IsFinishedReading
                tbLog.Text = tbLog.Text + telnet.Response
            End While
            Dim bteWrite As String = tbLog.Text
            File.WriteAllText("serverComm_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt", tbLog.Text)
        Else
            MessageBox.Show("Please Connect First", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Sub tbCommand_TextChanged(sender As Object, e As EventArgs) Handles tbCommand.TextChanged

    End Sub

    Private Sub tbCommand_KeyDown(sender As Object, e As KeyEventArgs) Handles tbCommand.KeyDown
        If e.KeyCode = Keys.Enter Then
            btnSend_ClickAsync(sender, e)
        End If
    End Sub

    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        For i = 0 To ResponseList.Count - 1
            tbLog.Text += ResponseList(i)
        Next
        ResponseList.Clear()
    End Sub
End Class
