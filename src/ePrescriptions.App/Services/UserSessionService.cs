using System;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.Services
{
    public interface IUserSessionService
    {
        User? CurrentUser { get; }
        Shift? CurrentShift { get; }
        bool IsAuthenticated { get; }
        bool IsLocked { get; }

        event Action? SessionChanged;
        event Action<bool>? LockStateChanged;

        bool Login(string username, string password, out string errorMessage);
        void Logout();
        void Lock();
        bool Unlock(string password, out string errorMessage);
        void SetCurrentShift(Shift? shift);
    }

    public class UserSessionService : IUserSessionService
    {
        private readonly UserRepository _userRepository;
        private readonly ShiftRepository _shiftRepository;

        private User? _currentUser;
        public User? CurrentUser => _currentUser;

        private Shift? _currentShift;
        public Shift? CurrentShift => _currentShift;

        public bool IsAuthenticated => _currentUser != null;

        private bool _isLocked = false;
        public bool IsLocked => _isLocked;

        public event Action? SessionChanged;
        public event Action<bool>? LockStateChanged;

        public UserSessionService(UserRepository userRepository, ShiftRepository shiftRepository)
        {
            _userRepository = userRepository;
            _shiftRepository = shiftRepository;
        }

        public bool Login(string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "يرجى إدخال اسم المستخدم وكلمة المرور.";
                return false;
            }

            var user = _userRepository.Authenticate(username, password);
            if (user == null)
            {
                errorMessage = "اسم المستخدم أو كلمة المرور غير صحيحة.";
                Logger.LogInfo($"محاولة تسجيل دخول فاشلة للمستخدم: {username}", "UserSessionService.Login");
                return false;
            }

            _currentUser = user;
            _isLocked = false;

            // جلب الوردية المفتوحة الحالية إن وجدت
            _currentShift = _shiftRepository.GetCurrentOpenShift(user.Id);

            Logger.LogInfo($"تسجيل دخول ناجح: {user.FullName} ({user.Role})", "UserSessionService.Login");
            SessionChanged?.Invoke();
            LockStateChanged?.Invoke(false);
            return true;
        }

        public void Logout()
        {
            if (_currentUser != null)
            {
                Logger.LogInfo($"تسجيل خروج للمستخدم: {_currentUser.FullName}", "UserSessionService.Logout");
            }

            _currentUser = null;
            _currentShift = null;
            _isLocked = false;

            SessionChanged?.Invoke();
            LockStateChanged?.Invoke(false);
        }

        public void Lock()
        {
            if (!IsAuthenticated) return;
            _isLocked = true;
            Logger.LogInfo($"تم قفل شاشة الصيدلية بواسطة: {_currentUser?.FullName}", "UserSessionService.Lock");
            LockStateChanged?.Invoke(true);
        }

        public bool Unlock(string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (_currentUser == null)
            {
                errorMessage = "لا يوجد مستخدم نشط.";
                return false;
            }

            if (PasswordHasher.VerifyPassword(password, _currentUser.PasswordHash))
            {
                _isLocked = false;
                Logger.LogInfo($"تم إلغاء قفل الشاشة بواسطة: {_currentUser.FullName}", "UserSessionService.Unlock");
                LockStateChanged?.Invoke(false);
                return true;
            }

            errorMessage = "كلمة المرور غير صحيحة.";
            return false;
        }

        public void SetCurrentShift(Shift? shift)
        {
            _currentShift = shift;
            SessionChanged?.Invoke();
        }
    }
}
