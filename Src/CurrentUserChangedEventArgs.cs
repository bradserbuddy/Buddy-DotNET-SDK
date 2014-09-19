using System;
using BuddySDK.Models;

namespace BuddySDK
{
    public class CurrentUserChangedEventArgs : EventArgs{

        public User PreviousUser {get; private set;}
        public User NewUser { get; set; }
        public CurrentUserChangedEventArgs(User newUser, User previousUser = null) {
            PreviousUser = previousUser;
            NewUser = newUser;
        }
    }
}

