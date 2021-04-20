﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow.IncomingDocumentBase;

namespace Sungero.Docflow
{
  partial class IncomingDocumentBaseCreatingFromServerHandler
  {

    public override void CreatingFrom(Sungero.Domain.CreatingFromEventArgs e)
    {
      base.CreatingFrom(e);
      
      if (_source.InResponseTo == null || !_source.InResponseTo.AccessRights.CanRead())
        e.Without(_info.Properties.InResponseTo);
    }
  }

  partial class IncomingDocumentBaseServerHandlers
  {

    public override void BeforeSave(Sungero.Domain.BeforeSaveEventArgs e)
    {
      base.BeforeSave(e);
      if (_obj.InResponseTo != null && _obj.InResponseTo.AccessRights.CanRead() && !_obj.Relations.GetRelatedFrom(Constants.Module.ResponseRelationName).Contains(_obj.InResponseTo))
        _obj.Relations.AddFromOrUpdate(Constants.Module.ResponseRelationName, _obj.State.Properties.InResponseTo.OriginalValue, _obj.InResponseTo);
    }
  }

  partial class IncomingDocumentBaseConvertingFromServerHandler
  {

    public override void ConvertingFrom(Sungero.Domain.ConvertingFromEventArgs e)
    {
      base.ConvertingFrom(e);
      
      // Для Входящих документов эл. обмена мапим Контрагента в Корреспондента.
      if (ExchangeDocuments.Is(_source))
        e.Map(_info.Properties.Correspondent, ExchangeDocuments.Info.Properties.Counterparty);
    }
  }

  partial class IncomingDocumentBaseFilteringServerHandler<T>
  {

    public virtual IQueryable<Sungero.Docflow.IDocumentKind> DocumentKindFiltering(IQueryable<Sungero.Docflow.IDocumentKind> query, Sungero.Domain.FilteringEventArgs e)
    {
      return query.Where(k => k.Status == CoreEntities.DatabookEntry.Status.Active &&
                         k.DocumentType.DocumentFlow == DocumentType.DocumentFlow.Incoming &&
                         k.DocumentType.IsRegistrationAllowed == true);
    }

    public virtual IQueryable<Sungero.Docflow.IDocumentRegister> DocumentRegisterFiltering(IQueryable<Sungero.Docflow.IDocumentRegister> query, Sungero.Domain.FilteringEventArgs e)
    {
      return Functions.DocumentRegister.GetAvailableDocumentRegisters(DocumentRegister.DocumentFlow.Incoming);
    }

    public override IQueryable<T> Filtering(IQueryable<T> query, Sungero.Domain.FilteringEventArgs e)
    {
      query = base.Filtering(query, e);
      
      if (_filter == null)
        return query;
      
      // Фильтр по журналу регистрации.
      if (_filter.DocumentRegister != null)
        query = query.Where(d => Equals(d.DocumentRegister, _filter.DocumentRegister));
      
      // Фильтр по виду документа.
      if (_filter.DocumentKind != null)
        query = query.Where(d => Equals(d.DocumentKind, _filter.DocumentKind));
      
      // Фильтр по статусу. Если все галочки включены, то нет смысла добавлять фильтр.
      if (_filter.Registered != _filter.NotRegistered)
        query = query
          .Where(d => _filter.Registered && d.RegistrationState == OfficialDocument.RegistrationState.Registered ||
                 _filter.NotRegistered && d.RegistrationState == OfficialDocument.RegistrationState.NotRegistered);
      
      // Фильтр по контрагенту.
      if (_filter.Counterparty != null)
        query = query.Where(d => Equals(d.Correspondent, _filter.Counterparty));
      
      // Фильтр "Подразделение".
      if (_filter.Department != null)
        query = query.Where(c => Equals(c.Department, _filter.Department));
      
      // Фильтр по интервалу времени
      var periodBegin = Calendar.UserToday.AddDays(-7);
      var periodEnd = Calendar.UserToday.EndOfDay();
      
      if (_filter.LastWeek)
        periodBegin = Calendar.UserToday.AddDays(-7);
      
      if (_filter.LastMonth)
        periodBegin = Calendar.UserToday.AddDays(-30);
      
      if (_filter.Last90Days)
        periodBegin = Calendar.UserToday.AddDays(-90);
      
      if (_filter.ManualPeriod)
      {
        periodBegin = _filter.DateRangeFrom ?? Calendar.SqlMinValue;
        periodEnd = _filter.DateRangeTo ?? Calendar.SqlMaxValue;
      }

      var serverPeriodBegin = Equals(Calendar.SqlMinValue, periodBegin) ? periodBegin : Docflow.PublicFunctions.Module.Remote.GetTenantDateTimeFromUserDay(periodBegin);
      var serverPeriodEnd = Equals(Calendar.SqlMaxValue, periodEnd) ? periodEnd : periodEnd.EndOfDay().FromUserTime();
      var clientPeriodEnd = !Equals(Calendar.SqlMaxValue, periodEnd) ? periodEnd.AddDays(1) : Calendar.SqlMaxValue;
      query = query.Where(j => (j.DocumentDate.Between(serverPeriodBegin, serverPeriodEnd) ||
                                j.DocumentDate == periodBegin) && j.DocumentDate != clientPeriodEnd);
      
      return query;
    }
  }

  partial class IncomingDocumentBaseInResponseToPropertyFilteringServerHandler<T>
  {

    public virtual IQueryable<T> InResponseToFiltering(IQueryable<T> query, Sungero.Domain.PropertyFilteringEventArgs e)
    {
      if (_obj.Correspondent != null)
        query = query.Where(l => l.Addressees.Any(a => Equals(a.Correspondent, _obj.Correspondent)));
      
      if (_obj.BusinessUnit != null)
        query = query.Where(l => Equals(_obj.BusinessUnit, l.BusinessUnit));
      
      return query;
    }
  }

}