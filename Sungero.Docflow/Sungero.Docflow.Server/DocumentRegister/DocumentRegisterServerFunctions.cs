﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow.DocumentRegister;
using Sungero.Domain.Shared;

namespace Sungero.Docflow.Server
{
  partial class DocumentRegisterFunctions
  {

    /// <summary>
    /// Получить список групп регистрации текущего пользователя.
    /// </summary>
    /// <returns>Список групп регистрации текущего пользователя.</returns>
    [Remote]
    public IQueryable<IRegistrationGroup> GetUsersRegistrationGroups()
    {
      return RegistrationGroups.GetAll().Where(r => r.RecipientLinks
                                               .Where(rec => Equals(rec.Member, Users.Current))
                                               .Any());
    }
    
    /// <summary>
    /// Получить список документов, зарегистрированных в данном журнале регистрации.
    /// </summary>
    /// <returns>Документы.</returns>
    [Remote]
    public IQueryable<IOfficialDocument> GetRegisteredDocuments()
    {
      return OfficialDocuments.GetAll(x => Equals(x.DocumentRegister, _obj));
    }
    
    /// <summary>
    /// Получить отфильтрованные журналы регистрации.
    /// </summary>
    /// <param name="direction">Документопоток.</param>
    /// <param name="isNotifiable">Регистрируемые \ Нумеруемые (True \ False).</param>
    /// <param name="forCurrentUser">Только для текущего сотрудника.</param>
    /// <returns>Журналы регистрации.</returns>
    [Remote(IsPure = true), Public]
    public static IQueryable<IDocumentRegister> GetFilteredDocumentRegisters(Enumeration direction, bool? isNotifiable, bool forCurrentUser)
    {
      var documentRegisters = DocumentRegisters.GetAll().Where(l => l.Status == CoreEntities.DatabookEntry.Status.Active)
        .Where(l => l.RegisterType == RegisterType.Numbering || !forCurrentUser || Recipients.AllRecipientIds.Contains(l.RegistrationGroup.Id));
      
      if (isNotifiable.HasValue)
        documentRegisters = isNotifiable.Value ?
          documentRegisters.Where(l => l.RegisterType == RegisterType.Registration) :
          documentRegisters.Where(l => l.RegisterType == RegisterType.Numbering);
      
      if (direction == Docflow.DocumentKind.DocumentFlow.Incoming)
        return documentRegisters.Where(l => l.DocumentFlow.Value == DocumentFlow.Incoming);
      
      if (direction == Docflow.DocumentKind.DocumentFlow.Outgoing)
        return documentRegisters.Where(l => l.DocumentFlow.Value == DocumentFlow.Outgoing);
      
      if (direction == Docflow.DocumentKind.DocumentFlow.Inner)
        return documentRegisters.Where(l => l.DocumentFlow.Value == DocumentFlow.Inner);
      
      if (direction == Docflow.DocumentKind.DocumentFlow.Contracts)
        return documentRegisters.Where(l => l.DocumentFlow.Value == DocumentFlow.Contracts);
      
      return null;
    }
    
    /// <summary>
    /// Получить журналы регистрации\резервирования по параметрам.
    /// </summary>
    /// <param name="kind">Вид.</param>
    /// <param name="unit">НОР.</param>
    /// <param name="department">Подразделение.</param>
    /// <param name="settingType">Тип нумерации.</param>
    /// <param name="forCurrentUser">Для текущего пользователя.</param>
    /// <returns>Журналы.</returns>
    [Public, Remote(IsPure = true)]
    public static List<IDocumentRegister> GetDocumentRegistersByParams(IDocumentKind kind, IBusinessUnit unit, IDepartment department,
                                                                       Enumeration? settingType, bool forCurrentUser)
    {
      // Результат сразу замыкается в ToList, так как иначе не транслируется в SQL, если вызывать на сервере.
      // Все журналы, указанные в настройках регистрации.
      var documentRegistersWithRegistrationSettings = RegistrationSettings
        .GetAllCached(s => s.Status == CoreEntities.DatabookEntry.Status.Active &&
                      s.SettingType == Docflow.RegistrationSetting.SettingType.Registration)
        .Select(s => s.DocumentRegister)
        .ToList();
      
      // Все журналы, кроме указанных в настройках регистрации.
      var documentRegistersWithoutRegistrationSettings = DocumentRegisters.GetAll()
        .Where(l => !documentRegistersWithRegistrationSettings.Contains(l));
      
      // Журналы, подходящие по непротиворечивым настройкам.
      var documentRegistersBySettings = Functions.RegistrationSetting
        .GetAvailableSettingsByParams(settingType, unit, kind, department)
        .Select(s => s.DocumentRegister)
        .ToList();
      
      // Журналы настроек регистрации, подходящих для текущего документа.
      var documentRegistersByRegistrationSettings = Functions.RegistrationSetting
        .GetAvailableSettingsByParams(Docflow.RegistrationSetting.SettingType.Registration, unit, kind, department)
        .Select(s => s.DocumentRegister)
        .ToList();
      
      // Получить все журналы по настройкам.
      documentRegistersByRegistrationSettings.AddRange(documentRegistersBySettings);
      
      // Фильтруем журналы по документопотоку. Только журналы регистрации.
      var result = Functions.DocumentRegister.GetFilteredDocumentRegisters(kind.DocumentFlow.Value, true, forCurrentUser)
        .Where(a => documentRegistersByRegistrationSettings.Contains(a) || documentRegistersWithoutRegistrationSettings.Contains(a)).ToList();
      
      // Для резервирования добавить настройки резервирования в обход проверки доступности журнала группе регистрации.
      // Делопроизводитель должен иметь возможность резервировать номер в документе, который не сможет зарегистрировать.
      if (settingType == Docflow.RegistrationSetting.SettingType.Reservation)
        result.AddRange(documentRegistersBySettings);

      return result.Distinct().ToList();
    }

    /// <summary>
    /// Получить доступные журналы.
    /// </summary>
    /// <param name="direction">Документопоток вида документа.</param>
    /// <returns>Журналы.</returns>
    public static IQueryable<IDocumentRegister> GetAvailableDocumentRegisters(Enumeration direction)
    {
      var documentRegisters = DocumentRegisters.GetAll()
        .Where(l => l.RegisterType == Docflow.DocumentRegister.RegisterType.Numbering || Recipients.AllRecipientIds.Contains(l.RegistrationGroup.Id));
      if (direction == Docflow.DocumentKind.DocumentFlow.Incoming)
        return documentRegisters.Where(l => l.DocumentFlow.Value == Docflow.DocumentRegister.DocumentFlow.Incoming);
      else if (direction == Docflow.DocumentKind.DocumentFlow.Outgoing)
        return documentRegisters.Where(l => l.DocumentFlow.Value == Docflow.DocumentRegister.DocumentFlow.Outgoing);
      else if (direction == Docflow.DocumentKind.DocumentFlow.Inner)
        return documentRegisters.Where(l => l.DocumentFlow.Value == Docflow.DocumentRegister.DocumentFlow.Inner);
      else if (direction == Docflow.DocumentKind.DocumentFlow.Contracts)
        return documentRegisters.Where(l => l.DocumentFlow.Value == Docflow.DocumentRegister.DocumentFlow.Contracts);
      else
        return null;
    }

    /// <summary>
    /// Есть ли зарегистрированные в журнале документы.
    /// </summary>
    /// <param name="documentRegister">Журнал регистрации.</param>
    /// <returns>Наличие документов.</returns>
    [Remote(IsPure = true)]
    public static bool HasRegisteredDocuments(IDocumentRegister documentRegister)
    {
      var command = string.Format(Queries.DocumentRegister.HasRegisteredDocuments, documentRegister.Id);
      
      var executionResult = Docflow.PublicFunctions.Module.ExecuteScalarSQLCommand(command);
      var result = 0;
      if (!(executionResult is DBNull) && executionResult != null)
        int.TryParse(executionResult.ToString(), out result);
      
      return result != 0;
    }
    
    /// <summary>
    /// Получить список документов, зарегистрированных в указанном периоде.
    /// </summary>
    /// <param name="documents">Документы.</param>
    /// <param name="periodBegin">Начало периода.</param>
    /// <param name="periodEnd">Конец периода.</param>
    /// <returns>Документы, зарегистрированные в промежутке между periodBegin и periodEnd.</returns>
    public static IQueryable<IOfficialDocument> FilterDocumentsByPeriod(IQueryable<IOfficialDocument> documents,
                                                                        DateTime? periodBegin, DateTime? periodEnd)
    {
      return documents
        .Where(d => !periodBegin.HasValue || d.RegistrationDate >= periodBegin)
        .Where(d => !periodEnd.HasValue || d.RegistrationDate <= periodEnd)
        .Where(d => d.Index != null && d.Index != 0);
    }

    /// <summary>
    /// Получить последний или первый индекс среди документов за указанный период.
    /// </summary>
    /// <param name="documents">Документы.</param>
    /// <param name="periodBegin">Начало периода.</param>
    /// <param name="periodEnd">Конец периода.</param>
    /// <param name="orderByDescending">True - последний индекс, false - первый индекс.</param>
    /// <returns>Индекс.</returns>
    public static int? GetIndex(IQueryable<IOfficialDocument> documents, DateTime? periodBegin, DateTime? periodEnd, bool orderByDescending)
    {
      var filteredDocuments = FilterDocumentsByPeriod(documents, periodBegin, periodEnd);
      return orderByDescending ?
        filteredDocuments.Select(d => d.Index).OrderByDescending(a => a).FirstOrDefault() :
        filteredDocuments.Select(d => d.Index).OrderBy(a => a).FirstOrDefault();
    }
    
    /// <summary>
    /// Получить делопроизводителей.
    /// </summary>
    /// <returns>Делопроизводители.</returns>
    [Remote(IsPure = true), Public]
    public static IRole GetClerks()
    {
      return Roles.GetAll().SingleOrDefault(r => r.Sid == Constants.Module.RoleGuid.ClerksRole);
    }
    
    /// <summary>
    /// Получить следующий регистрационный номер.
    /// </summary>
    /// <param name="registrationDate">Дата регистрации.</param>
    /// <param name="leadDocument">Ведущий документ.</param>
    /// <param name="department">Подразделение.</param>
    /// <param name="businessUnit">НОР.</param>
    /// <returns>Следующий регистрационный номер.</returns>
    [Public]
    public virtual int GetNextRegistrationNumber(DateTime registrationDate, int leadDocument = 0, int department = 0, int businessUnit = 0)
    {
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.LeadingDocument)
        leadDocument = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.Department)
        department = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.BusinessUnit)
        businessUnit = 0;
      
      using (var command = SQL.GetCurrentConnection().CreateCommand())
      {
        command.CommandType = System.Data.CommandType.StoredProcedure;
        
        command.CommandText = "Sungero_DocRegister_GetNextNumber";

        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@basevalue", 1);
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@docregid", _obj.Id);
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@newmonth", Functions.DocumentRegister.GetCurrentMonth(_obj, registrationDate));
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@newyear", Functions.DocumentRegister.GetCurrentYear(_obj, registrationDate));
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@leaddoc", leadDocument);
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@newquarter", Functions.DocumentRegister.GetCurrentQuarter(_obj, registrationDate));
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@newdep", department);
        Docflow.PublicFunctions.Module.AddIntegerParameterToCommand(command, "@newbusinessunit", businessUnit);
        
        var code = Docflow.PublicFunctions.Module.AddIntegerOutputParameterToCommand(command, "@result");

        command.ExecuteNonQuery();
        var registrationIndex = 0;
        int.TryParse(code.Value.ToString(), out registrationIndex);
        return registrationIndex;
      }
    }

    /// <summary>
    /// Получить текущий порядковый номер для журнала.
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <returns>Порядковый номер.</returns>
    [Remote(IsPure = true)]
    public virtual int GetCurrentNumber(DateTime date)
    {
      return Functions.DocumentRegister.GetCurrentNumber(_obj, date, 0, 0, 0);
    }
    
    /// <summary>
    /// Получить текущий порядковый номер для журнала.
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <param name="leadDocumentId">ID ведущего документа.</param>
    /// <param name="departmentId">ID подразделения.</param>
    /// <param name="businessUnitId">ID НОР.</param>
    /// <returns>Порядковый номер.</returns>
    [Remote(IsPure = true)]
    public virtual int GetCurrentNumber(DateTime date, int leadDocumentId, int departmentId, int businessUnitId)
    {
      var month = Functions.DocumentRegister.GetCurrentMonth(_obj, date);
      var year = Functions.DocumentRegister.GetCurrentYear(_obj, date);
      var quarter = Functions.DocumentRegister.GetCurrentQuarter(_obj, date);
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.LeadingDocument)
        leadDocumentId = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.Department)
        departmentId = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.BusinessUnit)
        businessUnitId = 0;
      
      var command = string.Format(Queries.DocumentRegister.GetCurrentNumber,
                                  _obj.Id, month, year, leadDocumentId, quarter, departmentId, businessUnitId);
      
      var executionResult = Docflow.PublicFunctions.Module.ExecuteScalarSQLCommand(command);
      var result = 0;
      if (!(executionResult is DBNull) && executionResult != null)
        int.TryParse(executionResult.ToString(), out result);
      
      return result;
    }
    
    /// <summary>
    /// Установить текущий номер документа для журнала.
    /// </summary>
    /// <param name="index">Текущий номер.</param>
    /// <param name="leadDocumentId">ID ведущего документа.</param>
    /// <param name="departmentId">ID подразделения.</param>
    /// <param name="businessUnitId">ID НОР.</param>
    /// <param name="date">Дата регистрации документа.</param>
    [Public, Remote]
    public virtual void SetCurrentNumber(int index, int leadDocumentId, int departmentId, int businessUnitId, DateTime date)
    {
      var month = Functions.DocumentRegister.GetCurrentMonth(_obj, date);
      var year = Functions.DocumentRegister.GetCurrentYear(_obj, date);
      var quarter = Functions.DocumentRegister.GetCurrentQuarter(_obj, date);
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.LeadingDocument)
        leadDocumentId = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.Department)
        departmentId = 0;
      
      if (_obj.NumberingSection != Docflow.DocumentRegister.NumberingSection.BusinessUnit)
        businessUnitId = 0;
      
      var commandText = string.Format(Queries.DocumentRegister.SetCurrentNumber,
                                      _obj.Id, index, month, year, leadDocumentId, quarter, departmentId, businessUnitId);
      
      Docflow.PublicFunctions.Module.ExecuteSQLCommand(commandText);
    }
    
    /// <summary>
    /// Получить следующий порядковый номер для журнала.
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <param name="leadDocumentId">Ведущий документ.</param>
    /// <param name="departmentId">Подразделение.</param>
    /// <param name="businessUnitId">НОР.</param>
    /// <param name="document">Текущий документ.</param>
    /// <returns>Порядковый номер.</returns>
    public virtual int? GetNextIndex(DateTime date, int leadDocumentId, int departmentId, int businessUnitId, IOfficialDocument document)
    {
      var index = Functions.DocumentRegister.GetCurrentNumber(_obj, date, leadDocumentId, departmentId, businessUnitId) + 1;
      var documentsList = this.GetOtherDocumentsInPeriodBySections(document, date)
        .Where(l => l.Index >= index)
        .Where(l => leadDocumentId == 0 || l.LeadingDocument != null && Equals(l.LeadingDocument.Id, leadDocumentId))
        .Where(l => departmentId == 0 || l.Department != null && Equals(l.Department.Id, departmentId))
        .Where(l => businessUnitId == 0 || l.BusinessUnit != null && Equals(l.BusinessUnit.Id, businessUnitId))
        .Select(l => l.Index)
        .ToList();
      
      // Вернуть следующий номер, если он не занят.
      if (!documentsList.Contains(index))
        return index;
      
      // Найти следующий незанятый номер.
      index = documentsList.Where(d => !documentsList.Contains(d.Value + 1)).Min(d => d.Value);
      return index + 1;
    }
    
    /// <summary>
    /// Получить следующий регистрационный номер.
    /// </summary>
    /// <param name="date">Дата регистрации.</param>
    /// <param name="leadDocumentId">ID ведущего документа.</param>
    /// <param name="document">Документ.</param>
    /// <param name="leadingDocumentNumber">Номер ведущего документа.</param>
    /// <param name="departmentId">ИД подразделения.</param>
    /// <param name="businessUnitId">ID НОР.</param>
    /// <param name="caseFileIndex">Индекс дела.</param>
    /// <param name="docKindCode">Код вида документа.</param>
    /// <param name="indexLeadingSymbol">Ведущий символ индекса.</param>
    /// <returns>Регистрационный номер.</returns>
    [Remote(IsPure = true)]
    public virtual string GetNextNumber(DateTime date, int leadDocumentId, IOfficialDocument document, string leadingDocumentNumber,
                                        int departmentId, int businessUnitId, string caseFileIndex, string docKindCode, string indexLeadingSymbol)
    {
      var index = Functions.DocumentRegister.GetNextIndex(_obj, date, leadDocumentId, departmentId, businessUnitId, document).ToString();
      
      var departmentCode = string.Empty;
      if (departmentId != 0)
      {
        var department = Departments.Get(departmentId);
        if (department != null)
          departmentCode = department.Code ?? string.Empty;
      }
      
      var businessUnitCode = string.Empty;
      if (businessUnitId != 0)
      {
        var businessUnit = BusinessUnits.Get(businessUnitId);
        if (businessUnit != null)
          businessUnitCode = businessUnit.Code ?? string.Empty;
      }
      
      var counterpartyCode = Functions.OfficialDocument.GetCounterpartyCode(document);
      
      var number = Functions.DocumentRegister.GenerateRegistrationNumber(_obj, date, index, leadingDocumentNumber,
                                                                         departmentCode, businessUnitCode, caseFileIndex, docKindCode, counterpartyCode, indexLeadingSymbol);
      return number;
    }

    /// <summary>
    /// Получить документы, зарегистрированные в журнале под тем же номером.
    /// </summary>
    /// <param name="doc">Документ.</param>
    /// <param name="registrationDate">Дата регистрации.</param>
    /// <param name="index">Индекс.</param>
    /// <returns>Документы, зарегистрированные в журнале под тем же номером.</returns>
    [Public]
    public virtual IQueryable<IOfficialDocument> GetSameNumberDocuments(IOfficialDocument doc, DateTime registrationDate, int index)
    {
      return this.GetOtherDocumentsInPeriodBySections(doc, registrationDate)
        .Where(l => l.Index == index);
    }
    
    /// <summary>
    /// Получить документы, зарегистрированные в журнале в тот же период по тем же разрезам.
    /// </summary>
    /// <param name="doc">Документ.</param>
    /// <param name="registrationDate">Дата регистрации.</param>
    /// <returns>Документы, зарегистрированные в журнале в тот же период по тем же разрезам.</returns>
    [Public]
    public virtual IQueryable<IOfficialDocument> GetOtherDocumentsInPeriodBySections(IOfficialDocument doc, DateTime registrationDate)
    {
      var periodBegin = Functions.DocumentRegister.GetBeginPeriod(_obj, registrationDate);
      var periodEnd = Functions.DocumentRegister.GetEndPeriod(_obj, registrationDate);
      
      var documents = OfficialDocuments.GetAll()
        .Where(l => !periodBegin.HasValue || l.RegistrationDate >= periodBegin)
        .Where(l => !periodEnd.HasValue || l.RegistrationDate <= periodEnd)
        .Where(l => l.DocumentRegister != null && Equals(l.DocumentRegister, _obj))
        .Where(l => l.Id != doc.Id);
      
      if (_obj.NumberingSection == Docflow.DocumentRegister.NumberingSection.LeadingDocument)
        documents = documents.Where(d => Equals(d.LeadingDocument, doc.LeadingDocument));
      
      if (_obj.NumberingSection == Docflow.DocumentRegister.NumberingSection.Department)
        documents = documents.Where(d => Equals(d.Department, doc.Department));
      
      if (_obj.NumberingSection == Docflow.DocumentRegister.NumberingSection.BusinessUnit)
        documents = documents.Where(d => Equals(d.BusinessUnit, doc.BusinessUnit));
      
      return documents;
    }
    
    /// <summary>
    /// Проверить регистрационный номер на уникальность.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="registrationNumber">Регистрационный номер.</param>
    /// <param name="index">Индекс.</param>
    /// <param name="registrationDate">Дата регистрации.</param>
    /// <param name="departmentCode">Код подразделения.</param>
    /// <param name="businessUnitCode">Код нашей организации.</param>
    /// <param name="caseFileIndex">Индекс дела.</param>
    /// <param name="docKindCode">Код вида документа.</param>
    /// <param name="counterpartyCode">Код контрагента.</param>
    /// <param name="leadDocumentId">ID ведущего документа.</param>
    /// <returns>True, если номер уникален, и false, если есть документы с таким же номером.</returns>
    [Public, Remote(IsPure = true)]
    public virtual bool IsRegistrationNumberUnique(IOfficialDocument document, string registrationNumber, int index,
                                                   DateTime registrationDate, string departmentCode, string businessUnitCode,
                                                   string caseFileIndex, string docKindCode, string counterpartyCode, int leadDocumentId)
    {
      var checkRegistrationNumberUnique = Functions.OfficialDocument.CheckRegistrationNumberUnique(document);

      // У финансовых документов и договоров явно отключаем требование уникальности рег. номера.
      if (!checkRegistrationNumberUnique)
        return true;

      var result = true;
      var leadDoc = OfficialDocuments.GetAll().FirstOrDefault(x => x.Id == leadDocumentId);
      var leadDocNumber = leadDoc == null ? string.Empty : leadDoc.RegistrationNumber;
      
      AccessRights.AllowRead(
        () =>
        {
          if (index == 0)
            // Параметр функции "Искать корректировочный постфикс" = true, если необходимо проверять рег. номер на уникальность.
            index = Functions.DocumentRegister.GetIndexFromRegistrationNumber(_obj, registrationDate, registrationNumber, departmentCode,
                                                                              businessUnitCode, caseFileIndex, docKindCode, counterpartyCode, leadDocNumber,
                                                                              checkRegistrationNumberUnique);
          
          var equalsIndexes = Functions.DocumentRegister.GetSameNumberDocuments(_obj, document, registrationDate, index).ToList();
          
          foreach (var equalsIndex in equalsIndexes)
          {
            // Параметр функции "Искать корректировочный постфикс" = true, если необходимо проверять рег. номер на уникальность.
            if (Functions.DocumentRegister.IsEqualsRegistrationNumbers(_obj, registrationDate, equalsIndex.RegistrationNumber,
                                                                       departmentCode, businessUnitCode, caseFileIndex, docKindCode, counterpartyCode, leadDocNumber,
                                                                       registrationNumber, checkRegistrationNumberUnique))
              result = false;
          }
        });
      
      return result;
    }
    
    /// <summary>
    /// Проверить, есть ли подразделения с незаполненным кодом.
    /// </summary>
    /// <returns>True, если есть хоть одно подразделение без кода.</returns>
    [Remote(IsPure = true)]
    public static bool HasDepartmentWithNullCode()
    {
      return Company.Departments.GetAll().Any(x => x.Status == CoreEntities.DatabookEntry.Status.Active && x.Code == null);
    }
    
    /// <summary>
    /// Проверить, есть ли наши организации с незаполненным кодом.
    /// </summary>
    /// <returns>True, если есть хоть одна наша организация без кода.</returns>
    [Remote(IsPure = true)]
    public static bool HasBusinessUnitWithNullCode()
    {
      return Company.BusinessUnits.GetAll().Any(x => x.Status == CoreEntities.DatabookEntry.Status.Active && x.Code == null);
    }
    
  }
}