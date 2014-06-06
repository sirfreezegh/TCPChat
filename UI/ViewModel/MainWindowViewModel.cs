﻿using Engine;
using Engine.API.StandardAPI;
using Engine.Model.Client;
using Engine.Model.Entities;
using Engine.Model.Server;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UI.Dialogs;
using UI.Infrastructure;
using UI.View;

namespace UI.ViewModel
{
  public class MainWindowViewModel : BaseViewModel
  {
    #region consts
    private const string ProgramName = "TCPChat";
    private const string ParamsError = "Ошибка входных данных.";
    private const string ClientNotCreated = "Клинет не соединен ни с каким сервером. Установите соединение.";
    private const string RegFailNickAlreadyExist = "Ник уже занят. Вы не зарегистрированны.";
    private const string RoomExitQuestion = "Вы действительно хотите выйти из комнаты?";
    private const string RoomCloseQuestion = "Вы точно хотите закрыть комнату?";
    private const string ServerDisableQuestion = "Вы точно хотите выключить сервер?";
    private const string FileMustDontExist = "Необходимо выбрать несуществующий файл.";
    private const string AllInRoom = "Все в комнате";

    private const int ClientMaxMessageLength = 100 * 1024;
    #endregion

    #region fields
    private MainWindow window;
    private int selectedRoomIndex;
    private RoomViewModel selectedRoom;
    #endregion

    #region properties
    public Dispatcher Dispatcher { get; private set; }
    public bool Alerts
    {
      get { return Settings.Current.Alerts; }
      set
      {
        Settings.Current.Alerts = value;

        OnPropertyChanged("Alerts");
      }
    }

    public RoomViewModel SelectedRoom
    {
      get { return selectedRoom; }
      set
      {
        selectedRoom = value;

        if (selectedRoom.Updated)
          selectedRoom.Updated = false;

        OnPropertyChanged("SelectedRoom");
      }
    }

    public int SelectedRoomIndex
    {
      get { return selectedRoomIndex; }
      set
      {
        if (value < 0)
        {
          selectedRoomIndex = 0;
          Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged("SelectedRoomIndex")), DispatcherPriority.Render);
          return;
        }

        selectedRoomIndex = value;

        OnPropertyChanged("SelectedRoomIndex");
      }
    }

    public ObservableCollection<RoomViewModel> Rooms { get; private set; }
    public ObservableCollection<UserViewModel> AllUsers { get; private set; }
    #endregion

    #region commands
    public ICommand EnableServerCommand { get; private set; }
    public ICommand DisableServerCommand { get; private set; }
    public ICommand ConnectCommand { get; private set; }
    public ICommand DisconnectCommand { get; private set; }
    public ICommand ExitCommand { get; private set; }
    public ICommand CreateRoomCommand { get; private set; }
    public ICommand DeleteRoomCommand { get; private set; }
    public ICommand ExitFromRoomCommand { get; private set; }
    public ICommand OpenFilesDialogCommand { get; private set; }
    public ICommand OpenAboutProgramCommand { get; private set; }
    #endregion

    #region constructors
    public MainWindowViewModel(MainWindow mainWindow)
    {
      window = mainWindow;
      window.Closed += WindowClosed;
      Rooms = new ObservableCollection<RoomViewModel>();
      AllUsers = new ObservableCollection<UserViewModel>();
      Dispatcher = Dispatcher.CurrentDispatcher;

      ClientModel.Connected += ClientConnect;
      ClientModel.ReceiveMessage += ClientReceiveMessage;
      ClientModel.ReceiveRegistrationResponse += ClientRegistration;
      ClientModel.RoomRefreshed += ClientRoomRefreshed;
      ClientModel.AsyncError += ClientAsyncError;
      ClientModel.RoomClosed += ClientRoomClosed;
      ClientModel.RoomOpened += ClientRoomOpened;

      ClearTabs();

      EnableServerCommand = new Command(EnableServer, Obj => ServerModel.Server == null);
      DisableServerCommand = new Command(DisableServer, Obj => ServerModel.Server != null);
      ConnectCommand = new Command(Connect, Obj => ClientModel.Client == null);
      DisconnectCommand = new Command(Disconnect, Obj => ClientModel.Client != null);
      ExitCommand = new Command(Obj => window.Close());
      CreateRoomCommand = new Command(CreateRoom, Obj => ClientModel.Client != null);
      DeleteRoomCommand = new Command(DeleteRoom, Obj => ClientModel.Client != null);
      ExitFromRoomCommand = new Command(ExitFromRoom, Obj => ClientModel.Client != null);
      OpenFilesDialogCommand = new Command(OpenFilesDialog, Obj => ClientModel.Client != null);
      OpenAboutProgramCommand = new Command(OpenAboutProgram);
    }
    #endregion

    #region command methods
    public void EnableServer(object obj)
    {
      ServerDialog dialog = new ServerDialog(Settings.Current.Nick, 
        Settings.Current.NickColor, 
        Settings.Current.Port, 
        Settings.Current.StateOfIPv6Protocol);

      if (dialog.ShowDialog() == true)
      {
        try
        {
          Settings.Current.Nick = dialog.Nick;
          Settings.Current.NickColor = dialog.NickColor;
          Settings.Current.Port = dialog.Port;
          Settings.Current.StateOfIPv6Protocol = dialog.UsingIPv6Protocol;

          ServerModel.Init(new StandardServerAPI());
          ServerModel.Server.Start(dialog.Port, dialog.UsingIPv6Protocol);

          ClientModel.Init(dialog.Nick, dialog.NickColor);
          ClientModel.Client.Connect(new IPEndPoint((dialog.UsingIPv6Protocol) ? IPAddress.IPv6Loopback : IPAddress.Loopback, dialog.Port));
        }
        catch (ArgumentException)
        {
          SelectedRoom.AddSystemMessage(ParamsError);

          if (ServerModel.IsInited)
            ServerModel.Reset();

          if (ClientModel.IsInited)
            ClientModel.Reset();
        }
      }
    }

    public void DisableServer(object obj)
    {
      if (MessageBox.Show(ServerDisableQuestion, ProgramName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
        return;

      ServerModel.Reset();

      if (ClientModel.IsInited)
        ClientModel.Reset();

      ClearTabs();
    }

    public void Connect(object obj)
    {
      ConnectDialog dialog = new ConnectDialog(
        Settings.Current.Nick, 
        Settings.Current.NickColor, 
        Settings.Current.Address, 
        Settings.Current.Port);

      if (dialog.ShowDialog() == true)
      {
        Settings.Current.Nick = dialog.Nick;
        Settings.Current.NickColor = dialog.NickColor;
        Settings.Current.Port = dialog.Port;
        Settings.Current.Address = dialog.Address.ToString();

        ClientModel.Init(dialog.Nick, dialog.NickColor);
        ClientModel.Client.Connect(new IPEndPoint(dialog.Address, dialog.Port));
      }
    }

    public void Disconnect(object obj)
    {
      try
      {
        ClientModel.API.Unregister();
      }
      catch (SocketException) { }

      ClientModel.Reset();

      ClearTabs();
    }

    public void CreateRoom(object obj)
    {
      try
      {
        CreateRoomDialog dialog = new CreateRoomDialog();
        if (dialog.ShowDialog() == true)
          ClientModel.API.CreateRoom(dialog.RoomName);
      }
      catch (SocketException se)
      {
        SelectedRoom.AddSystemMessage(se.Message);
      }
    }

    public void DeleteRoom(object obj)
    {
      try
      {
        if (MessageBox.Show(RoomCloseQuestion, ProgramName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
          return;

        ClientModel.API.DeleteRoom(SelectedRoom.Name);
      }
      catch (SocketException se)
      {
        SelectedRoom.AddSystemMessage(se.Message);
      }
    }

    public void ExitFromRoom(object obj)
    {
      try
      {
        if (MessageBox.Show(RoomExitQuestion, ProgramName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
          return;

        ClientModel.API.ExitFormRoom(SelectedRoom.Name);
      }
      catch (SocketException se)
      {
        SelectedRoom.AddSystemMessage(se.Message);
      }
    }

    public void OpenFilesDialog(object obj)
    {
      PostedFilesDialog dialog = new PostedFilesDialog();
      dialog.ShowDialog();
    }

    public void OpenAboutProgram(object obj)
    {
      AboutProgramDialog dialog = new AboutProgramDialog();
      dialog.ShowDialog();
    }
    #endregion

    #region client events
    private void ClientConnect(object sender, ConnectEventArgs e)
    {
      Dispatcher.Invoke(new Action<ConnectEventArgs>(args =>
      {
        if (args.Error != null)
        {
          SelectedRoom.AddSystemMessage(args.Error.Message);
          ClientModel.Reset();
          return;
        }

        ClientModel.API.Register();
      }), e);
    }

    private void ClientRegistration(object sender, RegistrationEventArgs e)
    {
      Dispatcher.Invoke(new Action<RegistrationEventArgs>(args =>
      {
        if (!args.Registered)
        {
          SelectedRoom.AddSystemMessage(RegFailNickAlreadyExist);
          ClientModel.Reset();
        }
      }), e);
    }

    private void ClientReceiveMessage(object sender, ReceiveMessageEventArgs e)
    {
      if (e.Type != MessageType.System && e.Type != MessageType.Private)
        return;

      Dispatcher.Invoke(new Action<ReceiveMessageEventArgs>(args =>
      {
        using (var client = ClientModel.Get())
          switch (args.Type)
          {
            case MessageType.Private:
              UserViewModel senderUser = AllUsers.Single(uvm => uvm.Info.Nick == args.Sender);
              UserViewModel receiverUser = AllUsers.Single(uvm => uvm.Info.Equals(client.User));
              SelectedRoom.AddPrivateMessage(senderUser, receiverUser, args.Message);
              break;

            case MessageType.System:
              SelectedRoom.AddSystemMessage(args.Message);
              break;
          }

        Alert();
      }), e);
    }

    private void ClientRoomRefreshed(object sender, RoomEventArgs e)
    {
      Dispatcher.Invoke(new Action<RoomEventArgs>(args =>
      {
        if (args.Room.Name == ServerModel.MainRoomName)
        {
          AllUsers.Clear();

          using(var client = ClientModel.Get())
            foreach (string nick in args.Room.Users)
            {
              User user = args.Users.Single(u => u.Equals(nick));

              if (user.Equals(client.User))
                AllUsers.Add(new UserViewModel(user, null) { IsClient = true });
              else
                AllUsers.Add(new UserViewModel(user, null));
            }
        }
      }), e);
    }

    private void ClientRoomOpened(object sender, RoomEventArgs e)
    {
      Dispatcher.Invoke(new Action<RoomEventArgs>(args =>
      {
        if (Rooms.FirstOrDefault(roomVM => roomVM.Name == args.Room.Name) != null)      
          return;
        
        RoomViewModel roomViewModel = new RoomViewModel(this, args.Room, e.Users);
        roomViewModel.Updated = true;
        Rooms.Add(roomViewModel);

        window.Alert();
      }), e);
    }

    private void ClientRoomClosed(object sender, RoomEventArgs e)
    {
      Dispatcher.Invoke(new Action<RoomEventArgs>(args =>
      {
        RoomViewModel roomViewModel = Rooms.FirstOrDefault(roomVM => roomVM.Name == args.Room.Name);

        if (roomViewModel == null)
          return;

        Rooms.Remove(roomViewModel);
        window.Alert();
      }), e);
    }

    private void ClientAsyncError(object sender, AsyncErrorEventArgs e)
    {
      Dispatcher.Invoke(new Action<AsyncErrorEventArgs>(args =>
      {
        if (args.Error.GetType() == typeof(APINotSupprtedException))
          ClientModel.Reset();

        SelectedRoom.AddSystemMessage(args.Error.Message);
      }), e);
    }
    #endregion

    #region helpers methods
    private void WindowClosed(object sender, EventArgs e)
    {
      if (ClientModel.IsInited)
      {
        try
        {
          ClientModel.API.Unregister();
        }
        catch (SocketException) { }

        ClientModel.Reset();
      }

      if (ServerModel.IsInited)
        ServerModel.Reset();

      Settings.SaveSettings();
    }

    public void Alert()
    {
      window.Alert();
    }

    private void ClearTabs()
    {
      AllUsers.Clear();
      Rooms.Clear();
      Rooms.Add(new RoomViewModel(this, new Room(null, ServerModel.MainRoomName), null));
      SelectedRoomIndex = 0;
    }
    #endregion
  }
}