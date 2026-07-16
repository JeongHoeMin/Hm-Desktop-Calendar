using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;

namespace HmDesktopCalendar.ViewModels;

public sealed class AccountViewModel : ViewModelBase
{
    private readonly IAccountSession _session;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmNewPassword = string.Empty;
    private string _deletePassword = string.Empty;
    private string _deleteConfirmation = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _sessionEnded;

    public AccountViewModel(IAccountSession session)
    {
        UserInfo user = session.User ?? throw new InvalidOperationException(
            "로그인한 사용자만 계정을 관리할 수 있습니다.");
        _session = session;
        UserId = user.Id;
        Email = user.Email;
    }

    public Guid UserId { get; }
    public string Email { get; }
    public string DeleteConfirmationHint =>
        $"삭제하려면 {Email}을(를) 정확히 입력하세요.";
    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetInput(ref _currentPassword, value);
    }
    public string NewPassword
    {
        get => _newPassword;
        set => SetInput(ref _newPassword, value);
    }
    public string ConfirmNewPassword
    {
        get => _confirmNewPassword;
        set => SetInput(ref _confirmNewPassword, value);
    }
    public string DeletePassword
    {
        get => _deletePassword;
        set => SetInput(ref _deletePassword, value);
    }
    public string DeleteConfirmation
    {
        get => _deleteConfirmation;
        set => SetInput(ref _deleteConfirmation, value);
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            NotifyActions();
        }
    }
    public bool SessionEnded
    {
        get => _sessionEnded;
        private set
        {
            if (!SetProperty(ref _sessionEnded, value)) return;
            NotifyActions();
        }
    }
    public bool CanChangePassword => !IsBusy && !SessionEnded &&
        CurrentPassword.Length >= 8 && NewPassword.Length >= 8 &&
        NewPassword == ConfirmNewPassword;
    public bool CanDeleteAccount => !IsBusy && !SessionEnded &&
        DeletePassword.Length >= 8 &&
        string.Equals(DeleteConfirmation.Trim(), Email,
            StringComparison.OrdinalIgnoreCase);
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!SetProperty(ref _statusMessage, value)) return;
            OnPropertyChanged(nameof(HasStatus));
        }
    }
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task<bool> ChangePasswordAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanChangePassword) return false;
        BeginRequest();
        try
        {
            await _session.ChangePasswordAsync(CurrentPassword, NewPassword,
                cancellationToken);
            CurrentPassword = NewPassword = ConfirmNewPassword = string.Empty;
            SessionEnded = true;
            StatusMessage = "비밀번호를 변경했습니다. 새 비밀번호로 다시 로그인하세요.";
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            ErrorMessage = MapError(exception);
            return false;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> DeleteAccountAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanDeleteAccount) return false;
        BeginRequest();
        try
        {
            await _session.DeleteAccountAsync(DeletePassword,
                cancellationToken);
            DeletePassword = DeleteConfirmation = string.Empty;
            SessionEnded = true;
            StatusMessage = "서버 계정과 원격 데이터를 삭제했습니다.";
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            ErrorMessage = MapError(exception);
            return false;
        }
        finally { IsBusy = false; }
    }

    public void SetExternalError(string message)
    {
        StatusMessage = string.Empty;
        ErrorMessage = message;
    }

    private void BeginRequest()
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        IsBusy = true;
    }

    private void SetInput(ref string field, string? value)
    {
        if (!SetProperty(ref field, value ?? string.Empty)) return;
        NotifyActions();
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanChangePassword));
        OnPropertyChanged(nameof(CanDeleteAccount));
    }

    private static string MapError(Exception exception) => exception switch
    {
        AuthApiException { StatusCode: HttpStatusCode.Unauthorized } =>
            "현재 비밀번호가 일치하지 않습니다.",
        AuthApiException { StatusCode: HttpStatusCode.TooManyRequests } =>
            "요청이 너무 많습니다. 잠시 후 다시 시도하세요.",
        AuthApiException auth => auth.Message,
        HttpRequestException => "서버에 연결할 수 없습니다. 네트워크와 서버 주소를 확인하세요.",
        _ => $"계정 요청에 실패했습니다: {exception.Message}"
    };
}
