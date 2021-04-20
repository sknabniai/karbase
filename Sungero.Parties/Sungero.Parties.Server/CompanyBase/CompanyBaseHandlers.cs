﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Parties.CompanyBase;

namespace Sungero.Parties
{
  partial class CompanyBaseCreatingFromServerHandler
  {

    public override void CreatingFrom(Sungero.Domain.CreatingFromEventArgs e)
    {
      base.CreatingFrom(e);
      e.Without(_info.Properties.IsCardReadOnly);
      
      // Если организация создана как копия НОР, то ее нельзя редактировать.
      // При копировании такой организации, комментарий в новую переносится не должен.
      if (_source.IsCardReadOnly == true)
        e.Without(_info.Properties.Note);
    }
  }

  partial class CompanyBaseHeadCompanyPropertyFilteringServerHandler<T>
  {
    public virtual IQueryable<T> HeadCompanyFiltering(IQueryable<T> query, Sungero.Domain.PropertyFilteringEventArgs e)
    {
      return query.Where(comp => !Equals(comp, _obj))
        .Where(comp => comp.HeadCompany == null || !Equals(comp.HeadCompany, _obj));
    }
  }

  partial class CompanyBaseServerHandlers
  {

    public override void AfterSave(Sungero.Domain.AfterSaveEventArgs e)
    {
      #region Создание контактов, пришедших с сервиса заполнения по ОГРН/ИНН
      
      var foundContacts = string.Empty;
      var companyContacts = Contacts.GetAll(contact => Equals(_obj, contact.Company));
      var hasActiveCompanyContacts = companyContacts.Any(contact => contact.Status == Contact.Status.Active);
      var hasContactsFromService = e.Params.TryGetValue(Constants.CompanyBase.FindedContactsInServiceParamName, out foundContacts) &&
        !string.IsNullOrWhiteSpace(foundContacts);
      
      if (!hasActiveCompanyContacts && hasContactsFromService && Contacts.AccessRights.CanCreate())
      {
        var contacts = foundContacts.Split(';');
        foreach (var contact in contacts)
        {
          var contactInfo = contact.Split('|');
          var name = contactInfo[0];
          var jobTitle = contactInfo.Length > 1 ? contactInfo[1] : string.Empty;
          var phone = contactInfo.Length > 2 ? contactInfo[2] : string.Empty;
          var isContactExist = companyContacts.Any(cont => cont.Name.ToLower() == name.ToLower());
          
          if (string.IsNullOrWhiteSpace(name) || isContactExist)
            continue;
          
          var companyContact = Contacts.Create();
          companyContact.Name = name;
          companyContact.JobTitle = jobTitle;
          companyContact.Phone = phone;
        }
      }
      e.Params.Remove(Constants.CompanyBase.FindedContactsInServiceParamName);
      
      #endregion
      
      base.AfterSave(e);
    }

    public override void BeforeSave(Sungero.Domain.BeforeSaveEventArgs e)
    {
      if (!string.IsNullOrEmpty(_obj.PSRN))
      {
        var newPsrn = _obj.PSRN.Trim();
        if (!_obj.PSRN.Equals(newPsrn, StringComparison.InvariantCultureIgnoreCase))
          _obj.PSRN = newPsrn;
      }
      
      if (!string.IsNullOrEmpty(_obj.TRRC))
      {
        var newTrrc = _obj.TRRC.Trim();
        if (!_obj.TRRC.Equals(newTrrc, StringComparison.InvariantCultureIgnoreCase))
          _obj.TRRC = newTrrc;
      }
      
      #region Проверить циклические ссылки в подчиненных организациях
      
      if (_obj.State.Properties.HeadCompany.IsChanged && _obj.HeadCompany != null)
      {
        var headCompany = _obj.HeadCompany;
        
        while (headCompany != null)
        {
          if (Equals(headCompany, _obj))
          {
            e.AddError(_obj.Info.Properties.HeadCompany, CompanyBases.Resources.HeadCompanyCyclicReference, _obj.Info.Properties.HeadCompany);
            break;
          }
          
          headCompany = headCompany.HeadCompany;
        }
      }
      
      #endregion
      
      base.BeforeSave(e);
    }

    public override void BeforeDelete(Sungero.Domain.BeforeDeleteEventArgs e)
    {
      // Запретить удаление организации в случае, если она зависит от нашей организации.
      if (_obj.IsCardReadOnly ?? false)
        e.AddError(CompanyBases.Resources.ErrorCantDeleteDependentCompany);
      
      base.BeforeDelete(e);
    }
  }
}