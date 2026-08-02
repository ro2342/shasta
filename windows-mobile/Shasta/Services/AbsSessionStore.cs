using System;
using Shasta.Models;
using Windows.Data.Json;
using Windows.Security.Credentials;

namespace Shasta.Services
{
    // Persists the logged-in session (server URL + tokens) in
    // PasswordVault — Windows' own encrypted credential store — instead of
    // a plain JSON file, because these are credentials, not app data.
    // Non-sensitive settings (theme, last opened library) still go through
    // LocalDataStore.
    public static class AbsSessionStore
    {
        private const string ResourceName = "Shasta.Session";
        private const string UserNameKey = "session";

        public static void Save(AbsSession session)
        {
            RemoveExisting();
            if (session == null)
            {
                return;
            }
            PasswordVault vault = new PasswordVault();
            vault.Add(new PasswordCredential(ResourceName, UserNameKey, session.ToJson().Stringify()));
        }

        public static AbsSession Load()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                PasswordCredential credential = vault.Retrieve(ResourceName, UserNameKey);
                credential.RetrievePassword();
                JsonObject obj = JsonObject.Parse(credential.Password);
                return AbsSession.Parse(obj);
            }
            catch (Exception)
            {
                // Not found, or corrupted — either way, no usable session.
                return null;
            }
        }

        public static void Clear()
        {
            RemoveExisting();
        }

        private static void RemoveExisting()
        {
            try
            {
                PasswordVault vault = new PasswordVault();
                PasswordCredential credential = vault.Retrieve(ResourceName, UserNameKey);
                vault.Remove(credential);
            }
            catch (Exception)
            {
                // Nothing to remove.
            }
        }
    }
}
