﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Parties.Person;

namespace Sungero.Parties.Shared
{
  partial class PersonFunctions
  {
    #region Проверка дублей
    
    /// <summary>
    /// Получить дубли персоны.
    /// </summary>
    /// <param name="excludeClosed">Исключить закрытые записи.</param>
    /// <returns>Дубли персоны.</returns>
    public override List<ICounterparty> GetDuplicates(bool excludeClosed)
    {
      // TODO Dmitriev_IA: На 53202, 69259 добавить поиск по имени.
      //                   По умолчанию для Person ищем по ИНН.
      return Functions.Module.Remote.GetDuplicateCounterparties(_obj.TIN, string.Empty, string.Empty, _obj.Id, excludeClosed)
        .Where(x => People.Is(x))
        .ToList();
    }
    
    #endregion
    
    /// <summary>
    /// Задать ФИО в соответствии с фамилией, именем и отчеством, а также Фамилию и инициалы.
    /// </summary>
    public void FillName()
    {
      if (string.IsNullOrEmpty(_obj.FirstName) || string.IsNullOrEmpty(_obj.LastName))
        return;

      using (TenantInfo.Culture.SwitchTo())
      {
        if (string.IsNullOrEmpty(_obj.MiddleName))
          _obj.Name = People.Resources.FullNameWithoutMiddleFormat(_obj.FirstName, _obj.LastName);
        else
          _obj.Name = People.Resources.FullNameFormat(_obj.FirstName, _obj.MiddleName, _obj.LastName);
      }
      
      // Короткое наименование для отчетов.
      _obj.ShortName = GetSurnameAndInitialsInTenantCulture(_obj.FirstName, _obj.MiddleName, _obj.LastName);
    }
    
    /// <summary>
    /// Получить ФИО в указанном падеже.
    /// </summary>
    /// <param name="declensionCase">Падеж.</param>
    /// <returns>ФИО.</returns>
    [Public]
    public virtual string GetFullName(Sungero.Core.DeclensionCase declensionCase)
    {
      var gender = CommonLibrary.Gender.NotDefined;
      if (_obj.Sex != null)
        gender = _obj.Sex == Sungero.Parties.Person.Sex.Female ?
          CommonLibrary.Gender.Feminine :
          CommonLibrary.Gender.Masculine;
      
      // Для фамилий типа Ардо (Иванова) неправильно склоняется через API. Баг 32895.
      var fullName = CommonLibrary.PersonFullName.Create(_obj.LastName, _obj.FirstName, _obj.MiddleName);
      var fullNameInDeclension = CommonLibrary.Padeg.ConvertPersonFullNameToTargetDeclension(fullName,
                                                                                             (CommonLibrary.DeclensionCase)(int)declensionCase,
                                                                                             gender);
      
      var middleName = string.IsNullOrWhiteSpace(_obj.MiddleName) ? string.Empty : fullNameInDeclension.MiddleName;      
      using (TenantInfo.Culture.SwitchTo())
        return Sungero.Parties.People.Resources.FullNameFormat(fullNameInDeclension.FirstName, middleName, fullNameInDeclension.LastName, "\u00A0");
    }

    /// <summary>
    /// Получить фамилию и инициалы в культуре тенанта.
    /// </summary>
    /// <param name="firstName">Имя.</param>
    /// <param name="middleName">Отчество.</param>
    /// <param name="lastName">Фамилия.</param>
    /// <returns>ФИО в коротком формате в локали тенанта.</returns>
    [Public]
    public static string GetSurnameAndInitialsInTenantCulture(string firstName, string middleName, string lastName)
    {
      using (TenantInfo.Culture.SwitchTo())
      {
        if (string.IsNullOrWhiteSpace(middleName))
          return People.Resources.ShortNameWithoutMiddleFormat(firstName.ToUpper()[0], lastName, "\u00A0");
        
        return People.Resources.ShortNameFormat(firstName.ToUpper()[0], middleName.ToUpper()[0], lastName, "\u00A0");
      }
    }
    
    /// <summary>
    /// Преобразует всю строку в строчные буквы, первый символ в прописную букву.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <returns>Скорректированная строка.</returns>
    public static string SetUppercaseFirstLetter(string source)
    {
      return string.Format("{0}{1}", source.Substring(0, 1).ToUpper(), source.Substring(1).ToLower());
    }
    
    /// <summary>
    /// Проверка ИНН на валидность.
    /// </summary>
    /// <param name="tin">Строка с ИНН.</param>
    /// <returns>Текст ошибки. Пустая строка для верного ИНН.</returns>
    public override string CheckTin(string tin)
    {
      return Functions.Counterparty.CheckTin(tin, false);
    }
  }
}