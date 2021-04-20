﻿using System;
using System.Collections.Generic;
using System.Linq;
using CommonLibrary;
using Sungero.Commons;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow.OfficialDocument;
using Sungero.Docflow.Structures.Module;
using Sungero.Docflow.Structures.OfficialDocument;
using Sungero.Domain.Client;

namespace Sungero.Docflow.Client
{
  partial class OfficialDocumentFunctions
  {
    #region Регистрация, нумерация и резервирование

    /// <summary>
    /// Вызвать диалог регистрации.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="dialogParams">Параметры диалога.</param>
    /// <returns>Результаты диалога регистрации.</returns>
    public static DialogResult RunRegistrationDialog(IOfficialDocument document, DialogParams dialogParams)
    {
      var leadDocumentId = dialogParams.LeadId;
      var leadDocumentNumber = dialogParams.LeadNumber;
      var currentRegistrationNumber = dialogParams.CurrentRegistrationNumber;
      var hasCurrentNumber = !string.IsNullOrWhiteSpace(currentRegistrationNumber);
      var currentRegistrationDate = dialogParams.CurrentRegistrationDate;
      var defaultDate = currentRegistrationDate.HasValue ? currentRegistrationDate.Value : Calendar.UserToday;
      var numberValidationDisabled = dialogParams.IsNumberValidationDisabled;
      var departmentId = dialogParams.DepartmentId;
      var departmentCode = dialogParams.DepartmentCode;
      var businessUnitCode = dialogParams.BusinessUnitCode;
      var businessUnitId = dialogParams.BusinessUnitId;
      var docKindCode = dialogParams.DocKindCode;
      var caseFileIndex = dialogParams.CaseFileIndex;
      var isClerk = dialogParams.IsClerk;
      var operation = dialogParams.Operation;
      var counterpartyCode = dialogParams.CounterpartyCode;
      
      var buttonName = string.Empty;
      var dialogTitle = string.Empty;
      var registrationNumberLabel = string.Empty;
      var helpCode = string.Empty;
      if (Equals(operation.Value, Docflow.RegistrationSetting.SettingType.Reservation.Value))
      {
        buttonName = OfficialDocuments.Resources.ReservationButtonName;
        dialogTitle = OfficialDocuments.Resources.ReservationTitle;
        registrationNumberLabel = OfficialDocuments.Resources.RegistrationNumber;
        helpCode = Constants.OfficialDocument.HelpCode.Reservation;
      }
      else if (Equals(operation.Value, Docflow.RegistrationSetting.SettingType.Numeration.Value))
      {
        buttonName = OfficialDocuments.Resources.AlternativeOkButtonName;
        dialogTitle = OfficialDocuments.Resources.AlternativeTitle;
        registrationNumberLabel = OfficialDocuments.Resources.AlternativeRegistrationNumber;
        helpCode = Constants.OfficialDocument.HelpCode.Numeration;
      }
      else
      {
        buttonName = OfficialDocuments.Resources.RegistrationButtonName;
        dialogTitle = OfficialDocuments.Resources.RegistrationTitle;
        registrationNumberLabel = OfficialDocuments.Resources.RegistrationNumber;
        helpCode = Constants.OfficialDocument.HelpCode.Registration;
      }
      
      var dialog = Dialogs.CreateInputDialog(dialogTitle);
      dialog.HelpCode = helpCode;
      var register = dialog.AddSelect(OfficialDocuments.Resources.DialogRegistrationLog, true, dialogParams.DefaultRegister).From(dialogParams.Registers);
      var date = dialog.AddDate(OfficialDocuments.Resources.DialogRegistrationDate, true, defaultDate);
      var isManual = dialog.AddBoolean(OfficialDocuments.Resources.IsManualNumber, hasCurrentNumber);
      var number = dialog.AddString(registrationNumberLabel, true)
        .WithLabel(OfficialDocuments.Resources.IsPreliminaryNumber)
        .MaxLength(document.Info.Properties.RegistrationNumber.Length);
      number.Value = hasCurrentNumber ? currentRegistrationNumber : dialogParams.NextNumber;
      number.IsLabelVisible = !hasCurrentNumber;
      var hyperlink = dialog.AddHyperlink(OfficialDocuments.Resources.LogNumberList);
      var button = dialog.Buttons.AddCustom(buttonName);
      dialog.Buttons.Default = button;
      dialog.Buttons.AddCancel();
      
      // Отчет доступен в документах с валидацией регномера при регистрации или резервировании делопроизводителем.
      hyperlink.IsVisible = operation == Docflow.RegistrationSetting.SettingType.Registration ||
        (operation == Docflow.RegistrationSetting.SettingType.Reservation && isClerk);
      
      // Номер по умолчанию недоступен для изменения.
      number.IsEnabled = hasCurrentNumber;
      
      // Журнал выбирать может только делопроизводитель, остальные видят, но менять не могут.
      register.IsEnabled = isClerk;
      register.IsVisible = operation == Docflow.RegistrationSetting.SettingType.Registration ||
        operation == Docflow.RegistrationSetting.SettingType.Reservation;
      
      dialog.SetOnRefresh((e) =>
                          {
                            if (date.Value != null && date.Value < Calendar.SqlMinValue)
                            {
                              e.AddError(Sungero.Docflow.OfficialDocuments.Resources.SetCorrectDate, date);
                              return;
                            }
                            hyperlink.IsEnabled = register.Value != null;
                            if (register.Value == null || !date.Value.HasValue)
                              return;
                            if (number.Value != null && number.Value.Length > document.Info.Properties.RegistrationNumber.Length)
                            {
                              var message = string.Format(Docflow.Resources.PropertyLengthError,
                                                          document.Info.Properties.RegistrationNumber.LocalizedName,
                                                          document.Info.Properties.RegistrationNumber.Length);
                              e.AddError(message, number);
                              return;
                            }
                            var numberSectionsError = Functions.DocumentRegister.CheckDocumentRegisterSections(register.Value, document);
                            var hasSectionsError = !string.IsNullOrWhiteSpace(numberSectionsError);
                            // Возможен корректировочный постфикс или нет (возможен, если необходимо проверять на уникальность).
                            var correctingPostfixInNumberIsAvailable = Functions.OfficialDocument.CheckRegistrationNumberUnique(document);
                            var numberFormatError = hasSectionsError
                              ? string.Empty
                              : Functions.DocumentRegister.CheckRegistrationNumberFormat(register.Value, date.Value, number.Value,
                                                                                         departmentCode, businessUnitCode, caseFileIndex, docKindCode, counterpartyCode,
                                                                                         leadDocumentNumber, correctingPostfixInNumberIsAvailable);
                            var hasFormatError = !string.IsNullOrWhiteSpace(numberFormatError);
                            
                            if (!hasSectionsError && !hasFormatError)
                              return;
                            
                            var error = hasSectionsError ? numberSectionsError : numberFormatError;
                            
                            if (numberValidationDisabled && isManual.Value.Value)
                              e.AddWarning(error);
                            else
                              e.AddError(error, number);
                          });
      dialog.SetOnButtonClick((e) =>
                              {
                                if (!Equals(e.Button, button))
                                  return;
                                
                                if (e.IsValid && isManual.Value.Value && date.Value.HasValue && register.Value != null)
                                {
                                  if (!Functions.DocumentRegister.Remote.IsRegistrationNumberUnique(register.Value, document, number.Value, 0,
                                                                                                    date.Value.Value, departmentCode, businessUnitCode,
                                                                                                    caseFileIndex, docKindCode, counterpartyCode, leadDocumentId))
                                    e.AddError(OfficialDocuments.Resources.RegistrationNumberIsNotUnique, number);
                                }
                              });

      register.SetOnValueChanged((e) =>
                                 {
                                   hyperlink.IsEnabled = e.NewValue != null && date.Value.HasValue;
                                   
                                   number.IsEnabled = isManual.Value.Value && e.NewValue != null && date.Value.HasValue;
                                   
                                   if (e.NewValue != null)
                                   {
                                     var previewDate = date.Value ?? Calendar.UserToday;
                                     var previewNumber = Functions.DocumentRegister.Remote
                                       .GetNextNumber(e.NewValue, previewDate, leadDocumentId, document, leadDocumentNumber, departmentId,
                                                      businessUnitId, caseFileIndex, docKindCode, Constants.OfficialDocument.DefaultIndexLeadingSymbol);
                                     number.Value = previewNumber;
                                   }
                                   else
                                     number.Value = string.Empty;
                                 });
      
      date.SetOnValueChanged((e) =>
                             {
                               if (e.NewValue != null && e.NewValue < Calendar.SqlMinValue)
                                 return;
                               
                               hyperlink.IsEnabled = e.NewValue != null && register.Value != null;
                               
                               number.IsEnabled = isManual.Value.Value && register.Value != null && e.NewValue.HasValue;
                               
                               if (!isManual.Value.Value)
                               {
                                 if (register.Value != null)
                                 {
                                   var previewDate = e.NewValue ?? Calendar.UserToday;
                                   var previewNumber = Functions.DocumentRegister.Remote
                                     .GetNextNumber(register.Value, previewDate, leadDocumentId, document, leadDocumentNumber, departmentId,
                                                    businessUnitId, caseFileIndex, docKindCode, Constants.OfficialDocument.DefaultIndexLeadingSymbol);
                                   number.Value = previewNumber;
                                 }
                                 else
                                   number.Value = string.Empty;
                               }
                             });
      
      isManual.SetOnValueChanged((e) =>
                                 {
                                   number.IsEnabled = e.NewValue.Value && register.Value != null && date.Value.HasValue;
                                   
                                   if (register.Value != null)
                                   {
                                     var previewDate = date.Value ?? Calendar.UserToday;
                                     var previewNumber = Functions.DocumentRegister.Remote
                                       .GetNextNumber(register.Value, previewDate, leadDocumentId, document, leadDocumentNumber, departmentId,
                                                      businessUnitId, caseFileIndex, docKindCode, Constants.OfficialDocument.DefaultIndexLeadingSymbol);
                                     number.Value = previewNumber;
                                   }
                                   else
                                     number.Value = string.Empty;
                                   number.IsLabelVisible = !e.NewValue.Value;
                                 });
      
      hyperlink.SetOnExecute(() =>
                             {
                               var report = Reports.GetSkippedNumbersReport();
                               report.DocumentRegisterId = register.Value.Id;
                               report.RegistrationDate = date.Value;
                               
                               // Если в журнале есть разрез по ведущему документу, подразделению или НОР, то заполнить из документа соответствующее свойство отчёта.
                               if (register.Value.NumberingSection == DocumentRegister.NumberingSection.LeadingDocument)
                                 report.LeadingDocument = document.LeadingDocument;
                               
                               if (register.Value.NumberingSection == DocumentRegister.NumberingSection.Department)
                                 report.Department = document.Department;
                               
                               if (register.Value.NumberingSection == DocumentRegister.NumberingSection.BusinessUnit)
                                 report.BusinessUnit = document.BusinessUnit;
                               
                               report.Open();
                             });
      
      if (dialog.Show() == button)
      {
        return DialogResult.Create(register.Value, date.Value.Value, isManual.Value.Value ? number.Value : string.Empty);
      }
      
      return null;
    }
    
    /// <summary>
    /// Зарегистрировать документ.
    /// </summary>
    /// <param name="e">Аргумент действия.</param>
    public void Register(Sungero.Domain.Client.ExecuteActionArgs e)
    {
      if (!e.Validate())
        return;
      
      // Регистрация документа с зарезервированным номером.
      if (_obj.RegistrationState == RegistrationState.Reserved)
      {
        this.RegisterWithReservedNumber(e);
        return;
      }
      
      // Список доступных журналов.
      var dialogParams = Functions.OfficialDocument.Remote.GetRegistrationDialogParams(_obj, Docflow.RegistrationSetting.SettingType.Registration);

      // Проверить возможность выполнения действия.
      // TODO Zamerov, 35685: может вернуться null вместо пустого списка.
      if (dialogParams.Registers == null || !dialogParams.Registers.Any())
      {
        e.AddError(Sungero.Docflow.Resources.NoDocumentRegistersAvailable);
        return;
      }

      // Вызвать диалог.
      var result = Functions.OfficialDocument.RunRegistrationDialog(_obj, dialogParams);
      
      if (result != null)
      {
        Functions.OfficialDocument.RegisterDocument(_obj, result.Register, result.Date, result.Number, false, true);
        
        Dialogs.NotifyMessage(Docflow.Resources.SuccessRegisterNotice);
      }
      return;
    }
    
    /// <summary>
    /// Зарегистрировать документ с зарезервированным номером.
    /// </summary>
    /// <param name="e">Аргумент действия.</param>
    public void RegisterWithReservedNumber(Sungero.Domain.Client.ExecuteActionArgs e)
    {
      // Валидация зарезервированных номеров, кроме тех, что добавлены в исключения.
      var numberValidationDisabled = Docflow.Functions.OfficialDocument.Remote.IsNumberValidationDisabled(_obj);
      if (!numberValidationDisabled)
      {
        var departmentCode = _obj.Department == null ? "0" : _obj.Department.Code;
        var businessUnitCode = _obj.BusinessUnit == null ? "0" : _obj.BusinessUnit.Code;
        var caseFileIndex = _obj.CaseFile == null ? "0" : _obj.CaseFile.Index;
        var documentKindCode = _obj.DocumentKind == null ? "0" : _obj.DocumentKind.Code;
        var counterpartyCode = Functions.OfficialDocument.GetCounterpartyCode(_obj);
        var leadDocNumber = _obj.LeadingDocument == null ? string.Empty : _obj.LeadingDocument.RegistrationNumber;
        // Возможен корректировочный постфикс или нет (возможен, если необходимо проверять на уникальность).
        var correctingPostfixInNumberIsAvailable = Functions.OfficialDocument.CheckRegistrationNumberUnique(_obj);
        var validationNumber = Functions.DocumentRegister.CheckRegistrationNumberFormat(_obj.DocumentRegister,
                                                                                        _obj.RegistrationDate,
                                                                                        _obj.RegistrationNumber,
                                                                                        departmentCode,
                                                                                        businessUnitCode,
                                                                                        caseFileIndex,
                                                                                        documentKindCode,
                                                                                        counterpartyCode,
                                                                                        leadDocNumber,
                                                                                        correctingPostfixInNumberIsAvailable);
        
        if (!string.IsNullOrEmpty(validationNumber))
        {
          e.AddError(validationNumber);
          return;
        }
      }
      
      // Список доступных журналов.
      var documentRegisters = Functions.OfficialDocument.GetDocumentRegistersByDocument(_obj, Docflow.RegistrationSetting.SettingType.Registration);
      
      // Проверить возможность выполнения действия.
      if (_obj.DocumentRegister != null && !documentRegisters.Contains(_obj.DocumentRegister))
      {
        e.AddError(Sungero.Docflow.Resources.NoRightToRegistrationInDocumentRegister);
        return;
      }

      var registrationData = string.Format("{0}:\n{1} - {2}\n{3} - {4}\n{5} - {6}",
                                           Docflow.Resources.ConfirmRegistrationWithFollowingData,
                                           Docflow.Resources.RegistrationNumber, _obj.RegistrationNumber,
                                           Docflow.Resources.RegistrationDate, _obj.RegistrationDate.Value.ToUserTime().ToShortDateString(),
                                           Docflow.Resources.DocumentRegister, _obj.DocumentRegister);

      // Диалог регистрации с зарезервированным номером.
      var reservedRegistration = Dialogs.CreateTaskDialog(Docflow.Resources.DocumentRegistration, registrationData);
      var reservedRegister = reservedRegistration.Buttons.AddCustom(Docflow.Resources.Register);
      reservedRegistration.Buttons.Default = reservedRegister;
      reservedRegistration.Buttons.AddCancel();

      if (reservedRegistration.Show() == reservedRegister)
      {
        Functions.OfficialDocument.RegisterDocument(_obj, _obj.DocumentRegister, _obj.RegistrationDate,
                                                    _obj.RegistrationNumber, false, true);
        Dialogs.NotifyMessage(Docflow.Resources.SuccessRegisterNotice);
      }
      else
        return;
    }
    
    /// <summary>
    /// Зарезервировать номер.
    /// </summary>
    /// <param name="e">Аргумент действия.</param>
    public void ReserveNumber(Sungero.Domain.Client.ExecuteActionArgs e)
    {
      if (!e.Validate())
        return;
      
      // Список доступных журналов.
      var dialogParams = Functions.OfficialDocument.Remote.GetRegistrationDialogParams(_obj, Docflow.RegistrationSetting.SettingType.Reservation);

      // Проверить возможность выполнения действия.
      // TODO Zamerov, 35685: может вернуться null вместо пустого списка.
      if (dialogParams.Registers == null || !dialogParams.Registers.Any())
      {
        e.AddError(Sungero.Docflow.Resources.NoDocumentRegistersAvailableForReserve);
        return;
      }

      if (dialogParams.Registers.Count > 1 && !dialogParams.IsClerk)
      {
        e.AddError(Sungero.Docflow.Resources.ReserveSettingsRequired);
        return;
      }

      // Вызвать диалог.
      var result = Functions.OfficialDocument.RunRegistrationDialog(_obj, dialogParams);

      if (result != null)
      {
        Functions.OfficialDocument.RegisterDocument(_obj, result.Register, result.Date, result.Number, true, true);
      }
      return;
    }

    /// <summary>
    /// Присвоить номер.
    /// </summary>
    /// <param name="e">Аргумент действия.</param>
    public void AssignNumber(Sungero.Domain.Client.ExecuteActionArgs e)
    {
      if (!e.Validate())
        return;

      // Список доступных журналов.
      var dialogParams = Functions.OfficialDocument.Remote.GetRegistrationDialogParams(_obj, Docflow.RegistrationSetting.SettingType.Numeration);

      // Проверить возможность выполнения действия.
      // TODO Zamerov, 35685: может вернуться null вместо пустого списка.
      if (dialogParams.Registers == null || !dialogParams.Registers.Any())
      {
        e.AddError(Sungero.Docflow.Resources.NumberingSettingsRequired);
        return;
      }

      if (dialogParams.Registers.Count > 1)
      {
        e.AddError(Sungero.Docflow.Resources.NumberingSettingsRequired);
        return;
      }

      // Вызвать диалог.
      var result = Functions.OfficialDocument.RunRegistrationDialog(_obj, dialogParams);
      
      if (result != null)
      {
        Functions.OfficialDocument.RegisterDocument(_obj, result.Register, result.Date, result.Number, false, true);
        
        Dialogs.NotifyMessage(Docflow.Resources.SuccessNumerationNotice);
      }
      return;
    }
    
    #endregion

    #region Отправка по email
    
    /// <summary>
    /// Получить связанные документы, имеющие версии.
    /// </summary>
    /// <returns>Список связанных документов.</returns>
    public virtual List<IOfficialDocument> GetRelatedDocumentsWithVersions()
    {
      var addendumRelatedDocuments = Docflow.Functions.OfficialDocument.Remote.GetRelatedDocumentsByRelationType(_obj, Docflow.Constants.Module.AddendumRelationName, true);
      var simpleRelatedDocuments = Docflow.Functions.OfficialDocument.Remote.GetRelatedDocumentsByRelationType(_obj, Docflow.Constants.Module.SimpleRelationName, true);
      
      addendumRelatedDocuments = addendumRelatedDocuments.OrderBy(x => x.Name).ToList();
      simpleRelatedDocuments = simpleRelatedDocuments.OrderBy(x => x.Name).ToList();
      
      var relatedDocuments = new List<IOfficialDocument>();
      relatedDocuments.AddRange(addendumRelatedDocuments);
      relatedDocuments.AddRange(simpleRelatedDocuments);
      
      // TODO Dmitirev_IA: Опасно для более 2000 документов.
      relatedDocuments = relatedDocuments.Distinct().ToList();
      return relatedDocuments;
    }
    
    /// <summary>
    /// Создание письма с вложенными документами.
    /// </summary>
    /// <param name="attachments">Список вложений.</param>
    public virtual void CreateEmail(List<IOfficialDocument> attachments)
    {
      var mail = MailClient.CreateMail();
      mail.Subject = Sungero.Docflow.OfficialDocuments.Resources.SendByEmailSubjectPrefixFormat(_obj.Name);
      mail.AddAttachment(_obj.LastVersion);
      if (attachments != null)
      {
        foreach (var relation in attachments)
          if (relation.HasVersions)
            mail.AddAttachment(relation.LastVersion);
      }
      mail.Show();
    }
    
    /// <summary>
    /// Получение информации о блокировке последней версии документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Информация о блокировке.</returns>
    public static Domain.Shared.LockInfo GetDocumentLastVersionLockInfo(IOfficialDocument document)
    {
      if (document == null || !document.HasVersions)
        return null;
      
      var body = document.LastVersion.Body;
      var publicBody = document.LastVersion.PublicBody;
      
      if (publicBody != null && publicBody.Id != null)
        return Locks.GetLockInfo(publicBody);
      
      return Locks.GetLockInfo(body);
    }
    
    /// <summary>
    /// Проверить информацию о блокировках тела и карточки документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Информация о блокировке.</returns>
    public static Domain.Shared.LockInfo GetDocumentLockInfo(IOfficialDocument document)
    {
      if (document == null)
        return null;
      
      var lockInfo = Functions.OfficialDocument.GetDocumentLastVersionLockInfo(document);
      if (lockInfo == null || !lockInfo.IsLocked)
        lockInfo = Locks.GetLockInfo(document);
      
      return lockInfo;
    }
    
    /// <summary>
    /// Проверить наличие блокировок последних версий документов.
    /// </summary>
    /// <param name="documents">Документы.</param>
    /// <returns>Если заблокированы - True, свободны - False.</returns>
    public bool HaveLastVersionLocks(List<IOfficialDocument> documents)
    {
      var lockInfos = new List<Domain.Shared.LockInfo>();
      
      var lockDocumentName = string.Empty;
      foreach (var doc in documents)
      {
        var lockInfo = GetDocumentLastVersionLockInfo(doc);
        if (lockInfo != null && lockInfo.IsLocked)
        {
          lockInfos.Add(lockInfo);
          lockDocumentName = doc.Name;
        }
      }
      
      if (lockInfos.Count == 0)
        return false;
      
      string description = null;
      var text = string.Empty;
      var title = string.Empty;
      
      if (lockInfos.Count == 1)
      {
        var info = lockInfos.First();
        if (info != null)
          description = info.LockedMessage;
        text = Sungero.Docflow.OfficialDocuments.Resources.VersionBeingSentMightBeOutdatedFormat(lockDocumentName);
        title = Sungero.Docflow.OfficialDocuments.Resources.DocumentIsBeingEdited;
      }
      else
      {
        text = Sungero.Docflow.OfficialDocuments.Resources.VersionsBeingSentMightBeOutdated;
        title = Sungero.Docflow.OfficialDocuments.Resources.SeveralDocumentsAreBeingEdited;
      }
      
      var errdialog = Dialogs.CreateTaskDialog(text, description, MessageType.Information, title);
      var retry = errdialog.Buttons.AddRetry();
      var send = errdialog.Buttons.AddCustom(Sungero.Docflow.OfficialDocuments.Resources.DialogButtonSend);
      var cancel = errdialog.Buttons.AddCancel();
      var result = errdialog.Show();
      
      if (result == retry)
      {
        return this.HaveLastVersionLocks(documents);
      }
      
      if (result == send)
        return false;
      else
        return true;
    }
    
    /// <summary>
    /// Выбор связанных документов для отправки и создания письма.
    /// </summary>
    /// <param name="relatedDocuments">Связанные документы.</param>
    public virtual void SelectRelatedDocumentsAndCreateEmail(List<IOfficialDocument> relatedDocuments)
    {
      if (relatedDocuments == null || relatedDocuments.Count == 0)
      {
        if (!this.HaveLastVersionLocks(new List<IOfficialDocument>() { _obj }))
          this.CreateEmail(relatedDocuments);
        return;
      }
      
      var dialog = Dialogs.CreateInputDialog(Sungero.Docflow.OfficialDocuments.Resources.SendByEmailDialogTitle);
      dialog.HelpCode = Constants.OfficialDocument.HelpCode.SendByEmail;
      dialog.Text = Sungero.Docflow.OfficialDocuments.Resources.SendByEmailDialogText;
      var mainDocument = dialog.AddSelect(Sungero.Docflow.OfficialDocuments.Resources.SendByEmailDialogMainDocument, true, _obj);
      mainDocument.IsEnabled = false;
      var selectedRelations = dialog
        .AddSelectMany(Sungero.Docflow.OfficialDocuments.Resources.SendByEmailDialogAttachments, false, OfficialDocuments.Null)
        .From(relatedDocuments);
      
      if (dialog.Show() == DialogButtons.Ok)
      {
        var allDocs = selectedRelations.Value.ToList();
        allDocs.Add(_obj);
        if (!this.HaveLastVersionLocks(allDocs))
          this.CreateEmail(selectedRelations.Value.ToList());
      }
    }
    
    #endregion
    
    #region Диалог создания поручений по телу документа
    
    /// <summary>
    /// Отобразить диалог создания поручений по документу.
    /// </summary>
    /// <param name="e">Аргументы действия, чтобы показывать ошибки валидации.</param>
    [Public]
    public virtual void CreateActionItemsFromDocumentDialog(Sungero.Core.IValidationArgs e)
    {
      var currentUser = Sungero.Company.Employees.Current;
      if (currentUser == null || currentUser.IsSystem == true)
      {
        Dialogs.NotifyMessage(OfficialDocuments.Resources.ActionItemCreationDialogLoginAsEmployeeError);
        return;
      }

      var dialogHeightNormal = ClientApplication.ApplicationType == ApplicationType.Desktop ? 200 : 160;
      var dialogHeightSmall = ClientApplication.ApplicationType == ApplicationType.Desktop ? 100 : 80;
      var existedActionItems = Functions.OfficialDocument.Remote.GetCreatedActionItems(_obj);
      var draftActionItems = existedActionItems.Where(x => x.Status == RecordManagement.ActionItemExecutionTask.Status.Draft &&
                                                      x.ParentTask == null && x.ParentAssignment == null && x.IsDraftResolution != true).ToList();
      
      var dialogItems = new List<RecordManagement.IActionItemExecutionTask>();
      var sendFailedItems = new List<RecordManagement.IActionItemExecutionTask>();
      
      var hasNotDeletedActionItems = false;
      var hasBeenSent = false;
      var beforeExitDialogText = string.Empty;
      
      var stepExistedItems = existedActionItems.Any();
      
      if (!stepExistedItems)
      {
        if (!this.TryCreateActionItemsFromDocument(dialogItems, e))
          return;
      }

      var dialog = Dialogs.CreateInputDialog(OfficialDocuments.Resources.ActionItemCreationDialog);
      dialog.Height = dialogHeightSmall;
      dialog.HelpCode = Constants.OfficialDocument.HelpCode.CreateActionItems;
      
      var next = dialog.Buttons.AddCustom(OfficialDocuments.Resources.ActionItemCreationDialogContinueButtonText);
      var close = dialog.Buttons.AddCustom(OfficialDocuments.Resources.ActionItemCreationDialogCloseButtonText);
      var cancel = dialog.Buttons.AddCustom(OfficialDocuments.Resources.CancelButtonText);
      var existedLink = dialog.AddHyperlink(OfficialDocuments.Resources.ActionItemCreationDialogExistedActionItems);
      existedLink.IsVisible = false;
      var failedLink = dialog.AddHyperlink(OfficialDocuments.Resources.ActionItemCreationDialogNotFilledActionItems);
      failedLink.IsVisible = false;
      
      // Принудительно увеличиваем ширину диалога для корректного отображения кнопок.
      var fakeControl = dialog.AddString("123", false);
      fakeControl.IsVisible = false;
      
      Action<CommonLibrary.InputDialogRefreshEventArgs> refresh = _ =>
      {
        if (stepExistedItems)
        {
          dialog.Height = dialogHeightNormal;
          next.Name = OfficialDocuments.Resources.ActionItemCreationDialogContinueButtonText;
          close.IsVisible = false;
          cancel.IsVisible = true;
          
          var descriptionText = string.Empty;
          var prefix = string.Empty;
          var actionItemDraftExist = OfficialDocuments.Resources.ActionItemCreationDialogDraftExists +
            Environment.NewLine + Environment.NewLine;
          
          if (draftActionItems.Any())
          {
            prefix = actionItemDraftExist;
            descriptionText += OfficialDocuments.Resources.ActionItemCreationDialogDraftWillBeDelete +
              Environment.NewLine + Environment.NewLine;
          }
          
          if (existedActionItems.Where(с => с.Status != RecordManagement.ActionItemExecutionTask.Status.Draft).Any())
          {
            prefix = actionItemDraftExist;
            descriptionText += ClientApplication.ApplicationType == ApplicationType.Desktop
              ? OfficialDocuments.Resources.ActionItemCreationDialogInProcessExists_Desktop
              : OfficialDocuments.Resources.ActionItemCreationDialogInProcessExists_Web;
          }
          
          if (existedActionItems.Count() == 0)
          {
            descriptionText += OfficialDocuments.Resources.ActionItemCreationDialogNoDraftAndInProgressExist +
              Environment.NewLine + Environment.NewLine;
            descriptionText += OfficialDocuments.Resources.ActionItemCreationDialogToCreateActionItemsPressNext;
          }
          
          dialog.Text = prefix + descriptionText;
          
          existedLink.IsVisible = existedActionItems.Any();
        }
        else
        {
          close.IsVisible = true;
          cancel.IsVisible = false;
          
          failedLink.IsVisible = NeedFillPropertiesItems(dialogItems).Any();

          var isAllSent = dialogItems.All(d => d.Status != RecordManagement.ActionItemExecutionTask.Status.Draft);
          next.IsVisible = !isAllSent;
          
          existedLink.IsVisible = dialogItems.Any();
          existedLink.Title = dialogItems.Any() ?
            OfficialDocuments.Resources.ActionItemCreationDialogCreatedActionItems :
            existedLink.Title;
          
          next.Name = OfficialDocuments.Resources.ActionItemCreationDialogSendForExecutionButtonText;
          close.Name  = isAllSent ?
            OfficialDocuments.Resources.ActionItemCreationDialogCloseButtonText :
            OfficialDocuments.Resources.ActionItemCreationDialogDeleteAndCloseButtonText;
          
          dialog.Text = string.Empty;
          if (hasNotDeletedActionItems)
            dialog.Text += OfficialDocuments.Resources.ActionItemCreationDialogSomeActionItemsCouldNotBeDeleted +
              Environment.NewLine + Environment.NewLine;

          if (!hasBeenSent && dialogItems.Any())
            dialog.Text += OfficialDocuments.Resources.ActionItemCreationDialogSuccessfullyCreated +
              Environment.NewLine + Environment.NewLine;
          
          dialog.Text += OfficialDocuments.Resources
            .ActionItemCreationDialogCreateCompletedActionItemsFormat(dialogItems.Count) + Environment.NewLine;
          
          if (dialogItems.Where(i => i.Status == RecordManagement.ActionItemExecutionTask.Status.InProcess).Any())
            dialog.Text += string.Format("  - {0} - {1}{2}", OfficialDocuments.Resources.ActionItemCreationDialogSended,
                                         dialogItems.Count(i => i.Status == RecordManagement.ActionItemExecutionTask.Status.InProcess),
                                         Environment.NewLine);
          
          if (NeedFillPropertiesItems(dialogItems).Any())
          {
            dialog.Height = dialogHeightNormal;
            var dialogItemsNeedfillProperties = NeedFillPropertiesItems(dialogItems).ToList();
            
            var notFilledAssigneeCount = dialogItemsNeedfillProperties.Count(t => t.Assignee == null);
            if (notFilledAssigneeCount != 0)
              dialog.Text += string.Format("  - {0} - {1}{2}", OfficialDocuments.Resources.ActionItemCreationDialogNeedFillAssignee,
                                           notFilledAssigneeCount, Environment.NewLine);
            
            var notFilledDedalineCount = dialogItemsNeedfillProperties.Count(t => t.Deadline == null);
            if (notFilledDedalineCount != 0)
              dialog.Text += string.Format("  - {0} - {1}{2}", OfficialDocuments.Resources.ActionItemCreationDialogNeedFillDeadline,
                                           notFilledDedalineCount, Environment.NewLine);
            
            var notFilledActionItemCount = dialogItemsNeedfillProperties.Count(t => string.IsNullOrWhiteSpace(t.ActionItem));
            if (notFilledActionItemCount != 0)
              dialog.Text += string.Format("  - {0} - {1}{2}", OfficialDocuments.Resources.ActionItemCreationDialogNeedFillSubject,
                                           notFilledActionItemCount, Environment.NewLine);
          }
          
          if (sendFailedItems.Any(t => t.Status == RecordManagement.ActionItemExecutionTask.Status.Draft) && hasBeenSent)
            dialog.Text += string.Format("  - {0} - {1}{2}", OfficialDocuments.Resources.ActionItemCreationDialogNeedSendManual,
                                         sendFailedItems.Count(t => t.Status == RecordManagement.ActionItemExecutionTask.Status.Draft),
                                         Environment.NewLine);
          
          // В Web перед закрытием диалога вызывается refresh. Исключаем кратковременное отображение некорректных данных в диалоге.
          if (!string.IsNullOrEmpty(beforeExitDialogText))
            dialog.Text = beforeExitDialogText;
        }
        
      };
      
      failedLink.SetOnExecute(() =>
                              {
                                // Список "Требуют заполнения".
                                NeedFillPropertiesItems(dialogItems).ToList().ShowModal();
                                dialogItems = RefreshDialogItems(dialogItems);
                                refresh.Invoke(null);
                              });
      
      existedLink.SetOnExecute(() =>
                               {
                                 // Список "Поручения".
                                 if (stepExistedItems)
                                 {
                                   existedActionItems.ToList().ShowModal();
                                   existedActionItems = Functions.OfficialDocument.Remote.GetCreatedActionItems(_obj);
                                   draftActionItems = existedActionItems
                                     .Where(m => m.Status == RecordManagement.ActionItemExecutionTask.Status.Draft &&
                                            m.ParentTask == null && m.ParentAssignment == null && m.IsDraftResolution != true).ToList();
                                   refresh.Invoke(null);
                                 }
                                 else
                                 {
                                   // Список "Созданные поручения".
                                   dialogItems.ToList().ShowModal();
                                   dialogItems = RefreshDialogItems(dialogItems);
                                   if (sendFailedItems.Count > 0)
                                     sendFailedItems = RefreshDialogItems(sendFailedItems);
                                   refresh.Invoke(null);
                                 }
                               });
      
      dialog.SetOnButtonClick(x =>
                              {
                                x.CloseAfterExecute = false;
                                
                                if (x.Button == next)
                                {
                                  if (stepExistedItems)
                                  {
                                    if (this.TryCreateActionItemsFromDocument(dialogItems, e))
                                    {
                                      if (!TryDeleteActionItemTasks(draftActionItems))
                                        hasNotDeletedActionItems = true;
                                      stepExistedItems = false;
                                      refresh.Invoke(null);
                                    }
                                    else
                                      x.CloseAfterExecute = true;
                                  }
                                  else
                                  {
                                    if (NeedFillPropertiesItems(dialogItems).Any())
                                    {
                                      x.AddError(OfficialDocuments.Resources.ActionItemCreationDialogNeedFillBeforeSending);
                                    }
                                    else
                                    {
                                      var forStartTasks = NoNeedFillPropertiesItems(dialogItems).ToList();
                                      sendFailedItems.Clear();
                                      foreach (var task in forStartTasks)
                                        ActionItemCreationDialogStartTask(task, sendFailedItems);
                                      
                                      hasBeenSent = true;
                                      
                                      if (sendFailedItems.Any(t => t.Status == RecordManagement.ActionItemExecutionTask.Status.Draft))
                                      {
                                        refresh.Invoke(null);
                                        x.AddError(OfficialDocuments.Resources.ActionItemCreationDialogNeedFillAndSendingManual);
                                      }
                                      
                                      if (dialogItems.All(i => i.Status != RecordManagement.ActionItemExecutionTask.Status.Draft))
                                      {
                                        x.CloseAfterExecute = true;
                                        beforeExitDialogText = dialog.Text;
                                        Dialogs.NotifyMessage(OfficialDocuments.Resources.ActionItemCreationDialogStartComplete);
                                      }
                                    }
                                  }
                                }
                                
                                if (x.Button == close)
                                {
                                  x.CloseAfterExecute = true;
                                  
                                  if (dialogItems.All(d => d.Status != RecordManagement.ActionItemExecutionTask.Status.Draft))
                                    return;
                                  
                                  if (TryDeleteActionItemTasks(dialogItems.Where(i => i.Status == RecordManagement.ActionItemExecutionTask.Status.Draft).ToList()))
                                    Dialogs.NotifyMessage(OfficialDocuments.Resources.ActionItemCreationDialogDraftWhereDeleted);
                                  else
                                  {
                                    hasNotDeletedActionItems = true;
                                    Dialogs.NotifyMessage(OfficialDocuments.Resources.ActionItemCreationDialogSomeActionItemsDraftNotDeleted);
                                  }
                                }
                              });
      dialog.SetOnRefresh(refresh);
      dialog.Show();
    }
    
    private bool TryCreateActionItemsFromDocument(List<RecordManagement.IActionItemExecutionTask> newActionItems,
                                                  IValidationArgs e)
    {
      try
      {
        newActionItems.Clear();
        newActionItems.AddRange(Functions.OfficialDocument.Remote.CreateActionItemsFromDocument(_obj));
      }
      catch (AppliedCodeException)
      {
        e.AddError(OfficialDocuments.Resources.ActionItemCreationDialogOnlyForWordDocument);
        return false;
      }
      if (newActionItems.Count == 0)
      {
        e.AddInformation(OfficialDocuments.Resources.ActionItemCreationDialogOnlyByTags);
        return false;
      }
      return true;
    }
    
    /// <summary>
    /// Удаление поручений, созданных по документу.
    /// </summary>
    /// <param name="tasks">Список задач, которые необходимо удалить.</param>
    /// <returns>True, если все поручения были успешно удалены.</returns>
    private static bool TryDeleteActionItemTasks(List<RecordManagement.IActionItemExecutionTask> tasks)
    {
      var hasFailedTask = false;
      // Удаление производится по одной задаче из-за платформенного бага 62797.
      foreach (var task in tasks)
      {
        if (!Functions.OfficialDocument.Remote.TryDeleteActionItemTask(task.Id))
          hasFailedTask = true;
      }
      
      return !hasFailedTask;
    }
    
    /// <summary>
    /// Обновление списка поручений.
    /// </summary>
    /// <param name="items">Список поручений.</param>
    /// <returns>Обновленный список поручений.</returns>
    private static List<RecordManagement.IActionItemExecutionTask> RefreshDialogItems(List<RecordManagement.IActionItemExecutionTask> items)
    {
      return Functions.OfficialDocument.Remote.GetActionItemsExecutionTasks(items.Select(t => t.Id).ToList());
    }
    
    private static IEnumerable<RecordManagement.IActionItemExecutionTask> NeedFillPropertiesItems(List<RecordManagement.IActionItemExecutionTask> items)
    {
      return items.Where(t => t.IsCompoundActionItem != true && t.Status == RecordManagement.ActionItemExecutionTask.Status.Draft &&
                         (t.Assignee == null || t.Deadline == null || string.IsNullOrWhiteSpace(t.ActionItem)));
    }
    
    private static IEnumerable<RecordManagement.IActionItemExecutionTask> NoNeedFillPropertiesItems(List<RecordManagement.IActionItemExecutionTask> items)
    {
      return items.Where(t => t.IsCompoundActionItem != true && t.Status == RecordManagement.ActionItemExecutionTask.Status.Draft &&
                         t.Assignee != null && t.Deadline != null && !string.IsNullOrWhiteSpace(t.ActionItem) || t.IsCompoundActionItem == true);
      
    }
    
    /// <summary>
    /// Старт задачи.
    /// </summary>
    /// <param name="actionItem">Задача.</param>
    /// <param name="sendFailedItems">Список задач с неудачным стартом.</param>
    private static void ActionItemCreationDialogStartTask(RecordManagement.IActionItemExecutionTask actionItem, List<RecordManagement.IActionItemExecutionTask> sendFailedItems)
    {
      if (actionItem.Status != RecordManagement.ActionItemExecutionTask.Status.Draft)
        return;
      
      try
      {
        if (RecordManagement.PublicFunctions.ActionItemExecutionTask.CheckOverdueActionItemExecutionTask(actionItem))
        {
          if (!sendFailedItems.Contains(actionItem))
            sendFailedItems.Add(actionItem);
        }
        else
          actionItem.Start();
      }
      catch (Exception ex)
      {
        Logger.Error("Task send failed", ex);
        if (!sendFailedItems.Contains(actionItem))
          sendFailedItems.Add(actionItem);
      }
    }
    
    #endregion
    
    #region Интеллектуальная обработка

    /// <summary>
    /// Включить режим верификации.
    /// </summary>
    [Public]
    public virtual void SwitchVerificationMode()
    {
      // Активировать / скрыть вкладку, подсветить свойства карточки и факты в теле только один раз при открытии.
      // Либо в событии Showing, либо в Refresh.
      // Вызов в Refresh необходим, т.к. при отмене изменений не вызывается Showing.
      if (!this.NeedHighlightPropertiesAndFacts())
        return;

      var formParams = ((Sungero.Domain.Shared.IExtendedEntity)_obj).Params;
      formParams.Add(PublicConstants.OfficialDocument.PropertiesAlreadyColoredParamName, true);
      
      // Активировать / скрыть вкладку.
      if (_obj.VerificationState != Docflow.OfficialDocument.VerificationState.InProcess)
      {
        _obj.State.Pages.PreviewPage.IsVisible = false;
        return;
      }
      _obj.State.Pages.PreviewPage.IsVisible = true;
      _obj.State.Pages.PreviewPage.Activate();
      
      this.SetHighlight();
    }
    
    /// <summary>
    /// Определить необходимость подсветки свойств в карточке и фактов.
    /// </summary>
    /// <returns>Признак необходимости подсветки.</returns>
    public virtual bool NeedHighlightPropertiesAndFacts()
    {
      var formParams = ((Sungero.Domain.Shared.IExtendedEntity)_obj).Params;
      return !formParams.ContainsKey(PublicConstants.OfficialDocument.PropertiesAlreadyColoredParamName);
    }

    /// <summary>
    /// Получить параметры отображения фокусировки подсветки.
    /// </summary>
    /// <returns>Параметры.</returns>
    public virtual IHighlightActivationStyle GetHighlightActivationStyle()
    {
      var highlightActivationStyle = HighlightActivationStyle.Create();
      highlightActivationStyle.UseBorder = PublicFunctions.Module.Remote.GetDocflowParamsStringValue(Constants.Module.HighlightActivationStyleParamNames.UseBorder);
      highlightActivationStyle.BorderColor = PublicFunctions.Module.Remote.GetDocflowParamsStringValue(Constants.Module.HighlightActivationStyleParamNames.BorderColor);
      highlightActivationStyle.BorderWidth = PublicFunctions.Module.Remote.GetDocflowParamsNumbericValue(Constants.Module.HighlightActivationStyleParamNames.BorderWidth);
      highlightActivationStyle.UseFilling = PublicFunctions.Module.Remote.GetDocflowParamsStringValue(Constants.Module.HighlightActivationStyleParamNames.UseFilling);
      highlightActivationStyle.FillingColor = PublicFunctions.Module.Remote.GetDocflowParamsStringValue(Constants.Module.HighlightActivationStyleParamNames.FillingColor);
      return highlightActivationStyle;
    }
    
    /// <summary>
    /// Получить список распознанных свойств документа.
    /// </summary>
    /// <param name="documentRecognitionInfo">Результат распознавания документа.</param>
    /// <returns>Список распознанных свойств документа.</returns>
    public virtual List<RecognizedProperty> GetRecognizedProperties(IEntityRecognitionInfo documentRecognitionInfo)
    {
      var result = new List<RecognizedProperty>();
      
      if (_obj == null || documentRecognitionInfo == null)
        return result;
      
      // Взять только заполненные свойства самого документа. Свойства-коллекции записываются через точку.
      var linkedFacts = documentRecognitionInfo.Facts
        .Where(x => !string.IsNullOrEmpty(x.PropertyName) && !x.PropertyName.Any(с => с == '.'));
      
      // Взять только неизмененные пользователем свойства.
      var type = _obj.GetType();
      foreach (var linkedFact in linkedFacts)
      {
        var propertyName = linkedFact.PropertyName;
        var property = type.GetProperties().Where(p => p.Name == propertyName).LastOrDefault();
        // Пропустить факт, если свойства не существует.
        if (property == null)
          continue;
        object propertyValue = property.GetValue(_obj);
        var propertyStringValue = Commons.PublicFunctions.Module.GetValueAsString(propertyValue);
        var propertyNotChanged = !string.IsNullOrWhiteSpace(propertyStringValue) &&
          Equals(propertyStringValue, linkedFact.PropertyValue) ||
          this.CanCompareAsNumbers(propertyStringValue, linkedFact.PropertyValue) &&
          this.CompareAsNumbers(propertyStringValue, linkedFact.PropertyValue) == 0;
        // Пропустить факт, если подобранное по нему свойство изменено.
        if (!propertyNotChanged)
          continue;
        // Для свойства собрать вместе все Positions.
        var recognizedProperty = result.FirstOrDefault(x => x.Name == propertyName);
        if (recognizedProperty == null)
          result.Add(RecognizedProperty.Create(propertyName, linkedFact.Probability, linkedFact.Position));
        else
          recognizedProperty.Position = string.Join(Constants.Module.PositionsDelimiter.ToString(),
                                                    recognizedProperty.Position,
                                                    linkedFact.Position);
      }
      
      return result;
    }

    /// <summary>
    /// Подсветить указанные свойства в карточке документа и факты в теле.
    /// </summary>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    public virtual void SetHighlightPropertiesAndFacts(IHighlightActivationStyle highlightActivationStyle)
    {
      var greaterConfidenceLimitColor = Sungero.Core.Colors.Common.LightGreen;
      var lessConfidenceLimitColor = Sungero.Core.Colors.Common.LightYellow;
      var greaterConfidenceLimitPreviewColor = Sungero.Core.Colors.Common.LightGreen;
      var lessConfidenceLimitPreviewColor = Sungero.Core.Colors.Common.LightYellow;
      
      var documentRecognitionInfo = Sungero.Commons.PublicFunctions.EntityRecognitionInfo.Remote.GetEntityRecognitionInfo(_obj);
      var propertyAttributes = this.GetRecognizedProperties(documentRecognitionInfo);
      var smartProcessingSettings = PublicFunctions.SmartProcessingSetting.GetSettings();
      if (smartProcessingSettings == null)
      {
        Logger.DebugFormat("Warning. Smart Processing Setting not found when trying to highlight document properties. (ID: {0})", _obj.Id);
        return;
      }
      var upperConfidenceLimit = smartProcessingSettings.UpperConfidenceLimit;
      
      foreach (var propertyAttribute in propertyAttributes)
      {
        var propertyColor = lessConfidenceLimitColor;
        var previewColor = lessConfidenceLimitPreviewColor;
        var propertyName = propertyAttribute.Name;
        var propertyInfo = _obj.Info.Properties.GetType().GetProperties().Where(p => p.Name == propertyName).LastOrDefault();
        var propertyInfoValue = (Sungero.Domain.Shared.IInternalPropertyInfo)propertyInfo.GetReflectionPropertyValue(_obj.Info.Properties);
        
        if (propertyAttribute.Probability != null &&
            propertyAttribute.Probability >= upperConfidenceLimit)
        {
          propertyColor = greaterConfidenceLimitColor;
          previewColor = greaterConfidenceLimitPreviewColor;
        }
        
        // Подсветка полей карточки.
        if (propertyInfoValue != null)
          _obj.State.Properties[propertyName].HighlightColor = propertyColor;
        
        // Подсветка фактов в теле документа.
        var position = propertyAttribute.Position;
        if (!string.IsNullOrWhiteSpace(position))
        {
          var fieldsPositions = position.Split(Constants.Module.PositionsDelimiter);
          foreach (var fieldPosition in fieldsPositions)
            this.HighlightFactInPreview(_obj.State.Controls.Preview, fieldPosition, previewColor,
                                        (Sungero.Domain.Shared.IPropertyInfo)propertyInfoValue, highlightActivationStyle);
        }
      }
    }

    /// <summary>
    /// Подсветить записи свойства-коллекции в карточке документа и факты в предпросмотре.
    /// </summary>
    /// <param name="previewControl">Контрол предпросмотра.</param>
    /// <param name="documentRecognitionInfo">Результат распознавания документа.</param>
    /// <param name="collection">Коллекция.</param>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    public virtual void HighlightCollection(Sungero.Domain.Shared.IPreviewControlState previewControl,
                                            IEntityRecognitionInfo documentRecognitionInfo,
                                            Sungero.Domain.Shared.IChildEntityCollection<Sungero.Domain.Shared.IChildEntity> collection,
                                            IHighlightActivationStyle highlightActivationStyle)
    {
      var greaterConfidenceLimitColor = Sungero.Core.Colors.Common.LightGreen;
      var lessConfidenceLimitColor = Sungero.Core.Colors.Common.LightYellow;
      var greaterConfidenceLimitPreviewColor = Sungero.Core.Colors.Common.LightGreen;
      var lessConfidenceLimitPreviewColor = Sungero.Core.Colors.Common.LightYellow;
      
      var smartProcessingSettings = PublicFunctions.SmartProcessingSetting.GetSettings();
      if (smartProcessingSettings == null)
      {
        Logger.DebugFormat("Warning. Smart Processing Setting not found when trying to highlight document properties. (ID: {0})", _obj.Id);
        return;
      }
      
      var upperConfidenceLimit = smartProcessingSettings.UpperConfidenceLimit;
      
      var recognizedFacts = documentRecognitionInfo.Facts;
      foreach (var record in collection)
      {
        var recognizedRecordFacts = recognizedFacts.Where(x => x.CollectionRecordId == record.Id &&
                                                          !string.IsNullOrEmpty(x.PropertyName) &&
                                                          x.PropertyName.Any(с => с == '.') &&
                                                          x.Probability != null);
        foreach (var recognizedRecordFact in recognizedRecordFacts)
        {
          var propertyColor = lessConfidenceLimitColor;
          var previewColor = lessConfidenceLimitPreviewColor;
          var probability = recognizedRecordFact.Probability;
          if (probability.HasValue && probability.Value >= upperConfidenceLimit)
          {
            propertyColor = greaterConfidenceLimitColor;
            previewColor = greaterConfidenceLimitPreviewColor;
          }

          var propertyName = recognizedRecordFact.PropertyName.Split('.').LastOrDefault();
          var property = record.GetType().GetProperties().Where(p => p.Name == propertyName).LastOrDefault();
          if (property != null)
          {
            object propertyValue = property.GetValue(record);
            var propertyStringValue = Commons.PublicFunctions.Module.GetValueAsString(propertyValue);
            if (!string.IsNullOrWhiteSpace(propertyStringValue) && Equals(propertyStringValue, recognizedRecordFact.PropertyValue))
            {
              record.State.Properties[propertyName].HighlightColor = propertyColor;
              var propertyInfo = record.Info.Properties.GetType().GetProperties().Where(p => p.Name == propertyName).LastOrDefault();
              var propertyInfoValue = (Sungero.Domain.Shared.IInternalPropertyInfo)propertyInfo.GetReflectionPropertyValue(record.Info.Properties);
              this.HighlightFactInPreview(previewControl, recognizedRecordFact.Position, previewColor,
                                          record, (Sungero.Domain.Shared.IPropertyInfo)propertyInfoValue,
                                          highlightActivationStyle);
            }
          }
        }
      }
    }

    /// <summary>
    /// Дополнительная подсветка.
    /// </summary>
    /// <param name="documentRecognitionInfo">Результат распознавания документа.</param>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    public virtual void SetAdditionalHighlight(IEntityRecognitionInfo documentRecognitionInfo,
                                               IHighlightActivationStyle highlightActivationStyle)
    {
      return;
    }

    /// <summary>
    /// Подсветить факт в предпросмотре.
    /// </summary>
    /// <param name="previewControl">Контрол предпросмотра.</param>
    /// <param name="position">Позиция.</param>
    /// <param name="color">Цвет.</param>
    public virtual void HighlightFactInPreview(Sungero.Domain.Shared.IPreviewControlState previewControl, string position, Sungero.Core.Color color)
    {
      var positions = position.Split(Constants.Module.PositionElementDelimiter);
      if (positions.Count() >= 7)
        previewControl.HighlightAreas.Add(color,
                                          int.Parse(positions[0]),
                                          double.Parse(positions[1].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                          double.Parse(positions[2].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                          double.Parse(positions[3].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                          double.Parse(positions[4].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                          double.Parse(positions[5].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                          double.Parse(positions[6].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture));
    }
    
    /// <summary>
    /// Подсветить факт в предпросмотре с фокусировкой по нажатию на свойство.
    /// </summary>
    /// <param name="previewControl">Контрол предпросмотра.</param>
    /// <param name="position">Позиция.</param>
    /// <param name="color">Цвет.</param>
    /// <param name="propertyInfo">Информация о свойстве.</param>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    public virtual void HighlightFactInPreview(Sungero.Domain.Shared.IPreviewControlState previewControl,
                                               string position, Sungero.Core.Color color, Sungero.Domain.Shared.IPropertyInfo propertyInfo,
                                               IHighlightActivationStyle highlightActivationStyle)
    {

      var area = this.AddHighlightArea(previewControl, position, color, highlightActivationStyle);
      if (area == null)
        return;
      
      area.SetRelatedProperty(propertyInfo);
    }
    
    /// <summary>
    /// Подсветить факт в предпросмотре с фокусировской по нажатию на свойство в табличной части.
    /// </summary>
    /// <param name="previewControl">Контрол предпросмотра.</param>
    /// <param name="position">Позиция.</param>
    /// <param name="color">Цвет.</param>
    /// <param name="childEntity">Свойство-коллекция.</param>
    /// <param name="childpropertyInfo">Информация о свойстве в коллекции.</param>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    public virtual void HighlightFactInPreview(Sungero.Domain.Shared.IPreviewControlState previewControl,
                                               string position, Sungero.Core.Color color, Sungero.Domain.Shared.IChildEntity childEntity,
                                               Sungero.Domain.Shared.IPropertyInfo childpropertyInfo,
                                               IHighlightActivationStyle highlightActivationStyle)
    {
      var area = this.AddHighlightArea(previewControl, position, color, highlightActivationStyle);
      if (area == null)
        return;
      
      area.SetRelatedChildCollectionProperty(childEntity, childpropertyInfo);
    }
    
    /// <summary>
    /// Добавить область выделения в предпросмотре.
    /// </summary>
    /// <param name="previewControl">Контрол предпросмотра.</param>
    /// <param name="position">Позиции.</param>
    /// <param name="color">Цвет.</param>
    /// <param name="highlightActivationStyle">Параметры отображения фокусировки подсветки.</param>
    /// <returns>Область выделения в предпросмотре.</returns>
    public virtual Sungero.Domain.Shared.IPreviewHighlight AddHighlightArea(Sungero.Domain.Shared.IPreviewControlState previewControl,
                                                                            string position, Sungero.Core.Color color,
                                                                            IHighlightActivationStyle highlightActivationStyle)
    {
      var positions = position.Split(Constants.Module.PositionElementDelimiter);
      if (positions.Count() >= 7)
      {
        var area = previewControl.HighlightAreas.Add(int.Parse(positions[0]),
                                                     double.Parse(positions[1].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                                     double.Parse(positions[2].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                                     double.Parse(positions[3].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                                     double.Parse(positions[4].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                                     double.Parse(positions[5].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture),
                                                     double.Parse(positions[6].Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture));
        // Установить подсветку согласно вероятности.
        area.Style.Color = color;
        
        // Установить поведение при фокусировке.
        // Рамка.
        var borderColor = TryParseColorCode(highlightActivationStyle.BorderColor);
        if (highlightActivationStyle.UseBorder != null)
        {
          area.ActivationStyle.BorderColor = borderColor != Sungero.Core.Colors.Empty ? borderColor : Colors.Common.Red;
          area.ActivationStyle.BorderWidth = highlightActivationStyle.BorderWidth > 0
            ? (int)highlightActivationStyle.BorderWidth
            : Constants.Module.HighlightActivationBorderDefaultWidth;
        }
        
        // Заливка цветом.
        var fillingColor = TryParseColorCode(highlightActivationStyle.FillingColor);
        if (highlightActivationStyle.UseFilling != null || highlightActivationStyle.UseBorder == null)
          area.ActivationStyle.Color = fillingColor != Sungero.Core.Colors.Empty ? fillingColor : Colors.Common.Blue;
        
        return area;
      }
      
      return null;
    }

    /// <summary>
    /// Получить цвет по коду.
    /// </summary>
    /// <param name="colorCode">Код цвета.</param>
    /// <returns>Цвет.</returns>
    public static Sungero.Core.Color TryParseColorCode(string colorCode)
    {
      var color = Sungero.Core.Colors.Empty;
      if (!string.IsNullOrWhiteSpace(colorCode))
      {
        try
        {
          color = Sungero.Core.Colors.Parse(colorCode);
        }
        catch
        {
        }
      }
      
      return color;
    }
    
    /// <summary>
    /// Проверить возможность проверки строк как чисел.
    /// </summary>
    /// <param name="firstString">Первая строка для сравнения.</param>
    /// <param name="secondString">Вторая строка для сравнения.</param>
    /// <returns>True - можно сравнивать как числа, иначе - False.</returns>
    private bool CanCompareAsNumbers(string firstString, string secondString)
    {
      
      firstString = firstString.Replace(',', '.');
      secondString = secondString.Replace(',', '.');
      double number;
      var numberStyles = System.Globalization.NumberStyles.Any;
      var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
      
      return double.TryParse(firstString, numberStyles, invariantCulture, out number) &&
        double.TryParse(secondString, numberStyles, invariantCulture, out number);
    }
    
    /// <summary>
    /// Сравнить строки как числа.
    /// </summary>
    /// <param name="firstString">Первая строка для сравнения.</param>
    /// <param name="secondString">Вторая строка для сравнения.</param>
    /// <returns>Значение, указывающее, каков относительный порядок сравниваемых объектов.</returns>
    /// <remarks>Ссылка: https://docs.microsoft.com/ru-ru/dotnet/api/system.icomparable.compareto?view=netframework-4.8.</remarks>
    private int CompareAsNumbers(string firstString, string secondString)
    {
      firstString = firstString.Replace(',', '.');
      secondString = secondString.Replace(',', '.');
      var numberStyles = System.Globalization.NumberStyles.Any;
      var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
      var firstNumber = double.Parse(firstString, numberStyles, invariantCulture);
      var secondNumber = double.Parse(secondString, numberStyles, invariantCulture);
      
      return firstNumber.CompareTo(secondNumber);
    }
    
    /// <summary>
    /// Управление подсветкой.
    /// </summary>
    public virtual void SetHighlight()
    {
      var highlightActivationStyle = this.GetHighlightActivationStyle();
      this.SetHighlightPropertiesAndFacts(highlightActivationStyle);
      
      var documentRecognitionInfo = Sungero.Commons.PublicFunctions.EntityRecognitionInfo.Remote.GetEntityRecognitionInfo(_obj);
      this.SetAdditionalHighlight(documentRecognitionInfo, highlightActivationStyle);
    }
    
    #endregion
    
    #region Действия отправки документов в заданиях
    
    /// <summary>
    /// Выбрать главный документ.
    /// </summary>
    /// <param name="documents">Документы.</param>
    /// <param name="probablyMainDocuments">Документы, которые вероятнее всего могут оказаться главными.</param>
    /// <returns>Документ.</returns>
    [Public]
    public static Sungero.Domain.Shared.IEntity ChooseMainDocument(List<Content.IElectronicDocument> documents,
                                                                   List<Content.IElectronicDocument> probablyMainDocuments)
    {
      // Если документ один, не показывать диалог выбора главного.
      if (documents.Count() == 1)
        return documents.FirstOrDefault();
      
      // Вывести диалог выбора главного документа.
      var dialogText = Resources.ChoosingDocumentDialogName;
      var dialog = Dialogs.CreateInputDialog(dialogText);
      var defaultDocument = documents.OrderByDescending(doc => probablyMainDocuments.Contains(doc)).FirstOrDefault();
      var mainDocument = dialog.AddSelect(Resources.MainDocumentField, true, defaultDocument).From(documents);
      dialog.Buttons.AddOkCancel();
      dialog.Buttons.Default = DialogButtons.Ok;
      var result = dialog.Show();
      
      if (result == DialogButtons.Cancel)
        return null;
      
      return mainDocument.Value;
    }
    
    /// <summary>
    /// Получение списка документов, к которым применимо действие.
    /// </summary>
    /// <param name="documents">Все вложения.</param>
    /// <param name="currentAction">Выбранное действие.</param>
    /// <returns>Список документов, к которым применимо действие.</returns>
    [Public]
    public static List<Content.IElectronicDocument> GetSuitableDocuments(List<Content.IElectronicDocument> documents, Domain.Shared.IActionInfo currentAction)
    {
      return documents.Where(doc => Docflow.OfficialDocuments.Is(doc) &&
                             !Docflow.ExchangeDocuments.Is(doc) &&
                             Docflow.PublicFunctions.OfficialDocument.CanExecuteSendAction(Docflow.OfficialDocuments.As(doc), currentAction)).ToList();
    }
    
    /// <summary>
    /// Определить, нужно ли добавлять документ во вложения задачи.
    /// </summary>
    /// <param name="attachment">Вложения.</param>
    /// <param name="mainOfficialDocument">Выбранный главный документ.</param>
    /// <returns>True, если нужно.</returns>
    [Public]
    public static bool NeedToAttachDocument(Content.IElectronicDocument attachment, Docflow.IOfficialDocument mainOfficialDocument)
    {
      if (Docflow.ExchangeDocuments.Is(attachment))
        return false;
      
      var addenda = mainOfficialDocument.Relations.GetRelated(Constants.Module.AddendumRelationName);
      if (addenda.Any(ad => Equals(ad.Id, attachment.Id)))
        return false;
      return true;
    }
    
    /// <summary>
    /// Проверить возможность выполнения действия отправки.
    /// </summary>
    /// <param name="actionInfo">Действие.</param>
    /// <returns>Признак доступности действия.</returns>
    [Public]
    public bool CanExecuteSendAction(Domain.Shared.IActionInfo actionInfo)
    {
      if (_obj.DocumentKind == null)
        return false;
      
      return _obj.DocumentKind.AvailableActions.Any(a => a.Action.ActionGuid == Functions.Module.GetActionGuid(actionInfo));
    }
    
    /// <summary>
    /// Если по документу уже были запущены задачи на согласование по регламенту,
    /// то с помощью диалога определить, нужно ли создавать ещё одну.
    /// </summary>
    /// <returns>True, если нужно создать еще одну задачу на согласование. Иначе false.</returns>
    [Public]
    public bool NeedCreateApprovalTask()
    {
      var result = true;
      
      var createdTasks = Docflow.PublicFunctions.Module.Remote.GetApprovalTasks(_obj);
      if (createdTasks.Any())
      {
        result = false;
        
        var dialog = Dialogs.CreateTaskDialog(OfficialDocuments.Resources.ContinueCreationApprovalTaskQuestion,
                                              OfficialDocuments.Resources.DocumentHasApprovalTasks,
                                              MessageType.Question);
        var showButton = dialog.Buttons.AddCustom(OfficialDocuments.Resources.ShowButtonText);
        var continueButton = dialog.Buttons.AddCustom(OfficialDocuments.Resources.SendButtonText);
        dialog.Buttons.AddCancel();
        
        CommonLibrary.DialogButton dialogResult = showButton;
        
        while (dialogResult == showButton)
        {
          dialogResult = dialog.Show();
          
          if (dialogResult.Equals(showButton))
          {
            if (createdTasks.Count() == 1)
              createdTasks.Single().ShowModal();
            else
              createdTasks.ShowModal();
          }
          if (dialogResult.Equals(continueButton))
            result = true;
        }
      }
      
      return result;
    }
    
    /// <summary>
    /// Если по документу уже были запущены задачи на рассмотрение,
    /// то с помощью диалога определить, нужно ли создавать ещё одну.
    /// </summary>
    /// <returns>True, если нужно создать еще одну задачу на рассмотрение. Иначе false.</returns>
    [Public]
    public bool NeedCreateReviewTask()
    {
      var result = true;
      
      var createdTasks = Docflow.PublicFunctions.Module.Remote.GetReviewTasks(_obj);
      if (createdTasks.Any())
      {
        result = false;
        
        var dialog = Dialogs.CreateTaskDialog(OfficialDocuments.Resources.ContinueCreationReviewTaskQuestion,
                                              OfficialDocuments.Resources.DocumentHasReviewTasks,
                                              MessageType.Question);
        var showButton = dialog.Buttons.AddCustom(OfficialDocuments.Resources.ShowButtonText);
        var continueButton = dialog.Buttons.AddCustom(OfficialDocuments.Resources.SendButtonText);
        dialog.Buttons.AddCancel();
        
        CommonLibrary.DialogButton dialogResult = showButton;
        
        while (dialogResult == showButton)
        {
          dialogResult = dialog.Show();
          
          if (dialogResult.Equals(showButton))
          {
            if (createdTasks.Count() == 1)
              createdTasks.Single().ShowModal();
            else
              createdTasks.ShowModal();
          }
          if (dialogResult.Equals(continueButton))
            result = true;
        }
      }
      
      return result;
    }
    
    #endregion
    
    #region Смена типа
    
    /// <summary>
    /// Сменить тип документа.
    /// </summary>
    /// <param name="types">Типы документов, на которые можно сменить.</param>
    /// <returns>Сконвертированный документ.</returns>
    [Public]
    public virtual Sungero.Docflow.IOfficialDocument ChangeDocumentType(List<Domain.Shared.IEntityInfo> types)
    {
      Sungero.Docflow.IOfficialDocument convertedDoc = null;
      
      // Запретить смену типа, если документ или его тело заблокировано.
      var isCalledByDocument = CallContext.CalledDirectlyFrom(OfficialDocuments.Info);
      if (isCalledByDocument && Functions.Module.IsLockedByOther(_obj) ||
          !isCalledByDocument && Functions.Module.IsLocked(_obj) ||
          Functions.Module.VersionIsLocked(_obj.Versions.ToList()))
      {
        Dialogs.ShowMessage(Docflow.ExchangeDocuments.Resources.ChangeDocumentTypeLockError,
                            MessageType.Error);
        return convertedDoc;
      }
      
      // Открыть диалог по смене типа.
      var title = ExchangeDocuments.Resources.TypeChange;
      var dialog = Dialogs.CreateSelectTypeDialog(title, types.ToArray());
      if (dialog.Show() == DialogButtons.Ok)
        convertedDoc = OfficialDocuments.As(_obj.ConvertTo(dialog.SelectedType));
      
      return convertedDoc;
    }
    
    /// <summary>
    /// Получить список типов документов, доступных для смены типа.
    /// </summary>
    /// <returns>Список типов документов, доступных для смены типа.</returns>
    public virtual List<Domain.Shared.IEntityInfo> GetTypesAvailableForChange()
    {
      return new List<Domain.Shared.IEntityInfo>();
    }
    
    #endregion
    
    /// <summary>
    /// Получить текст для отметки документа устаревшим.
    /// </summary>
    /// <returns>Текст для диалога прекращения согласования.</returns>
    public virtual string GetTextToMarkDocumentAsObsolete()
    {
      return OfficialDocuments.Resources.MarkDocumentAsObsolete;
    }
    
    /// <summary>
    /// Показывать сводку по документу в заданиях на согласование и подписание.
    /// </summary>
    /// <returns>True, если в заданиях нужно показывать сводку по документу.</returns>
    [Public]
    public virtual bool NeedViewDocumentSummary()
    {
      return false;
    }
  }
}