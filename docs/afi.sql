USE [ConnectorDB]
GO
/****** Object:  StoredProcedure [dbo].[USP_FetchClaims]    Script Date: 4/21/2026 9:48:09 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

--exec USP_FetchClaims 'INS102','01/06/2023','30/06/2023'
                
ALTER PROCEDURE [dbo].[USP_FetchClaims]  
          @InsuranceCode   nvarchar(50),               
          @_FromDate       nvarchar(50),               
          @_ToDate         nvarchar(50)       
AS
  BEGIN
      DECLARE @From_Dt_fmt NVARCHAR(50),
              @To_Date_fmt NVARCHAR(50),
              @BatNo       NVARCHAR(10),
              @UserID        INT,
              @UserName      NVARCHAR(100),
              @Fmonth        BIT


		SET @UserID=''
		SET @UserName=''
		SET @Fmonth=0
        SET @From_Dt_fmt =(SELECT Substring(@_FromDate, 7, 4)
                                + Substring(@_FromDate, 4, 2)
                                + Substring(@_FromDate, 1, 2))

        SET @To_Date_fmt =(SELECT Substring(@_ToDate, 7, 4)
                                + Substring(@_ToDate, 4, 2)
                                + Substring(@_ToDate, 1, 2))
        SET @BatNo = (( RIGHT(@_FromDate, 4) * 100 ))
        SET @BatNo=@BatNo + Substring(@_FromDate, 4, 2)

      DECLARE @Followup INT
	        

    Begin Try

	exec sp_deletebatch @InsuranceCode,@_FromDate,@_ToDate

	 Print 'Old batch deletion done'


        insert into [DHS-NPHIES].dbo.DHSClaim_Header (
            patientname,
            patientno,
            memberid,
            PreAuthid,
            specialty,
            clinicaldata,
            claimtype,
            referind,
            emerind,
            submitby,
            payto,
            Temperature,
            RespiratoryRate,
            BloodPressure,
            Height,
            [Weight],
            Pulse,
            InternalNotes,
            companycode,
            policyholdername,
            policyholderno,
            class,
            InvoiceNumber,
            InvoiceDate,
            TreatmentCountryCode,
            DestinationCode,
            ProviderCode,
            ClaimedAmount,
            ClaimedAmountSAR,
            TotalNetAmount,
            TotalDiscount,
            TotalDeductible,
            BenType,
            BenHead,
            provider_dhscode,
            BatchNo,
            BatchDate,
            batchStartDate,
            BatchEndDate,
            PatientID,
            DOB,
            Age,
            Nationality,
            DurationOFIllness,
            ChiefComplaint,
            MaritalStatus,
            PatientGender,
            VisitType,
            Plan_Type,
            MSVRef,
            UserId,
            IssueNo,
            UserName,
            PayerRef,
            ErrorMessage,
            MobileNo,
            PMAUserName,
            DoctorCode,
			[patientidtype],
			[SubscriberRelationship],
			[DHSC],
			[AdmissionDate],
			[DischargeDate],
			[AdmissionNo],
			[AdmissionSpecialty],
			[AdmissionType],
			[RoomNumber],
			[BedNumber],
			[DischargeSpecialty],
			[LengthOfStay],
			[AdmissionWeight],
			[DischargeMode],
			[IDno],
			[EmergencyArrivalCode],
	        [EmergencyStartDate],
	        [EmergencyWaitingTime],
	        [TriageDate],
	        [AdmissionTypeID],
	        [DischargeDepositionsTypeID],
	        [EnconuterTypeID],
	        [CareTypeID],
	        [TriageCategoryTypeID],
	        [EmergencyDepositionTypeID],
	        [SignificantSigns],
	        [MainSymptoms],
	        [DischargeSummary],
	        [OtherConditions],
	        [EncounterStatus],
	        [EncounterNo],
	        [ActIncidentCode],
	        [EpisodeNumber],
	        [CCHI],
	        [BranchID],
	        [NewBorn],
	        [OxygenSaturation],
	        [BirthWeight],
	        [FetchDate],
	        [RelatedClaim],
	        [Priority],
			PatientOccupation,      --MDS
            CauseOfDeath,           --MDS
	        TreatmentPlan,          --MDS
	        PatientHistory,         --MDS
	        PhysicalExamination,    --MDS
	        HistoryOfPresentIllness,--MDS
	        PatientReligion,        --MDS
	        MorphologyCode,         --MDS
	        InvestigationResult     --MDS
            )

        select 
            patientname,
            patientno,
            memberid,
            PreAuthid,
            specialty,
            clinicaldata,
            claimtype,
            referind ,
            emerind,
            submitby,
            payto,
            Temperature,
            RespiratoryRate,
            BloodPressure,
            Height,
            [Weight],
            Pulse,
            InternalNotes,
            companycode,
            policyholdername,
            policyholderno,
            class,
            InvoiceNumber,
            InvoiceDate,
            TreatmentCountryCode,
            DestinationCode,
            ProviderCode,
            ClaimedAmount,
            ClaimedAmountSAR,
            TotalNetAmount,
            TotalDiscount,
            TotalDeductible,
            BenType,
            BenHead,
            provider_dhscode,
            BatchNo,
            BatchDate,
            @From_Dt_fmt,
            @To_Date_fmt,
            PatientID,
            DOB,
            Age,
            Nationality,
            DurationOFIllness,
            ChiefComplaint,
            MaritalStatus,
            PatientGender,
            VisitType,
            Plan_Type,
            MSVRef,
            UserId,
            IssueNo,
            UserName,
            PayerRef,
            ErrorMessage,
            MobileNo,
            PMAUserName,
            DoctorCode,
			[patientidtype],
			[SubscriberRelationship],
			[DHSC],
			[AdmissionDate],
			[DischargeDate],
			[AdmissionNo],
			[AdmissionSpecialty],
			[AdmissionType],
			[RoomNumber],
			[BedNumber],
			[DischargeSpecialty],
			[LengthOfStay],
			[AdmissionWeight],
			[DischargeMode],
			[IDno],
			[EmergencyArrivalCode],
	        [EmergencyStartDate],
	        [EmergencyWaitingTime],
	        [TriageDate],
	        [AdmissionTypeID],
	        [DischargeDepositionsTypeID],
	        [EnconuterTypeID],
	        [CareTypeID],
	        [TriageCategoryTypeID],
	        [EmergencyDepositionTypeID],
	        [SignificantSigns],
	        [MainSymptoms],
	        [DischargeSummary],
	        [OtherConditions],
	        [EncounterStatus],
	        [EncounterNo],
	        [ActIncidentCode],
	        [EpisodeNumber],
	        [CCHI],
	        [BranchID],
	        [NewBorn],
	        [OxygenSaturation],
	        [BirthWeight],
	        GETDATE(),
	        [RelatedClaim],
	        [Priority],
			PatientOccupation,      --MDS
            case when CauseOfDeath = 'Null' then null else CauseOfDeath end,           --MDS
	        TreatmentPlan,          --MDS
	        PatientHistory,         --MDS
	        PhysicalExamination,    --MDS
	        HistoryOfPresentIllness,--MDS
	        PatientReligion,        --MDS
	        MorphologyCode,         --MDS
	        InvestigationResult     --MDS

        from DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
             where CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.batchStartDate) <= @To_Date_fmt
              and ch.companycode = @InsuranceCode


        print 'Header Done'

		------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


        Insert into [DHS-NPHIES].dbo.DHSService_Details
        (
            itemRefernce,
            TreatmentFromDate,
            TreatmentToDate,
            NumberOfIncidents,
            LineClaimedAmount,
            CoInsurance,
            CoPay,
            LineItemDiscount,
            NetAmount,
            VatIndicator,
            VatPercentage,
            PatientVatAmount,
            NetVatAmount,
            ServiceCode,
            ServiceDescription,
            MediCode,
            ToothNo,
            SubmissionReasonCode,
            TreatmentTypeIndicator,
            ServiceGategory,
			[CashAmount],
	        [PBMDuration],
	        [PBMTimes],
	        [PBMUnit],
	        [PBMPer],
	        [PBMUnitType],
	        [ServiceEventType],
	        [ARDRG],
	        [ServiceType],
	        [PreAuthId],
	        [DoctorCode],
	        [UnitPrice],
	        [DiscPercentage],
			[ErrorMessage],
			PharmacistSelectionReason,    -- EBP
			PharmacistSubstitute,         -- EBP
			ScientificCode,               -- EBP
			DiagnosisCode,                -- EBP
			ScientificCodeAbsenceReason,  -- EBP
			Maternity,                    --MDS
            ProIdClaim
        )
        select itemRefernce,
            TreatmentFromDate,
            TreatmentToDate,
            NumberOfIncidents,
            LineClaimedAmount,
            CoInsurance,
            CoPay,
            LineItemDiscount,
            NetAmount,
            VatIndicator,
            VatPercentage,
            PatientVatAmount,
            NetVatAmount,
            ServiceCode,
            ServiceDescription,
            MediCode,
            ToothNo,
            SubmissionReasonCode,
            TreatmentTypeIndicator,
            ServiceGategory,
			[CashAmount],
	        [PBMDuration],
	        [PBMTimes],
	        CEILING([PBMUnit]),
	        [PBMPer],
	        [PBMUnitType],
	        [ServiceEventType],
	        [ARDRG],
	        [ServiceType],
	        ch.[PreAuthId],
	        ch.[DoctorCode],
	        [UnitPrice],
	        [DiscPercentage],
			ss.[ErrorMessage],
			PharmacistSelectionReason,    -- EBP
			PharmacistSubstitute,         -- EBP
			ScientificCode,               -- EBP
			DiagnosisCode,                -- EBP
			ScientificCodeAbsenceReason,  -- EBP
			Maternity,                    --MDS
            dch.ProIdClaim

        from DHS.[DHS_NPHIES].[dbo].DHSService_Details ss
            inner join DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                on ss.proidclaim = ch.proidclaim
            inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where CONVERT(date, treatmentfromdate) >= @From_Dt_fmt
              and CONVERT(date, treatmenttodate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt
              and dch.companycode = @InsuranceCode

        print 'Services Done'
---------------------------------------------------------------------------------------------------------------------

UPDATE H                                           
            SET H.ClaimedAmount = Result.LinINTERFACEedAmount,                                          
                H.TotalDiscount = Result.LineItemDiscount,                                          
                H.TotalDeductible = Result.CoInsurance,                                          
                H.TotalNetAmount = Result.NetAmount                                          
            FROM [DHS-NPHIES].dbo.DHSClaim_Header H                                            
               INNER JOIN ( select S.ProIDClaim,                                          
                        sum(S.LineClaimedAmount) as LinINTERFACEedAmount,                                          
                        sum(S.CoInsurance) as CoInsurance ,                           
                        sum(S.LineItemDiscount) as LineItemDiscount,                                          
                        sum(S.NetAmount) as NetAmount from [DHS-NPHIES].dbo.DHSService_Details S group by S.ProIDClaim ) Result                                             
                ON H.ProIdClaim = Result.ProIdClaim                                          
             where  H.CompanyCode = @InsuranceCode
		              and H.batchStartDate >= @From_Dt_fmt                                           
                      and  H.batchStartDate <= @To_Date_fmt 

	print 'Financials Done'	

---------------------------------------------------------------------------------------------------------------------------------------------------------

       insert into [DHS-NPHIES].dbo.DHSDiagnosis_Details(DiagnosisCode,DiagnosisDesc,DiagnosisTypeID,DiagnosisDate,IllnessTypeIndicator,DiagnosisType,ProIdClaim) 
	   select upper(DiagnosisCode),DiagnosisDesc,DiagnosisTypeID,DiagnosisDate,IllnessTypeIndicator,DiagnosisTypeID,dch.ProIdClaim  

        from DHS.[DHS_NPHIES].[dbo].DHSDiagnosis_Details dd
            inner join DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                on dd.proidclaim = ch.proidclaim
            inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where ch.CompanyCode = @InsuranceCode
              and CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.batchStartDate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt

        print 'Diagnosis Done'

----------------------------------------------------------------------------------------------------------------------------------------------------------------
       insert into [DHS-NPHIES].dbo.DHSLab_Details(LabProfile,LabTestName,LabResult,    
                              LabUnits,LabLow,LabHigh,    
                               LabSection,VisitDate,ResultDate,ServiceCode,LoincCode,ProIdClaim)    
                  select left(LabProfile,200),left(LabTestName,30),left(LabResult,200),    
                       left(LabUnits,50),left(LabLow,50),left(LabHigh,50),    
                         left(LabSection,50),VisitDate,ResultDate,ServiceCode,LoincCode,dch.ProIdClaim 

        from DHS.[DHS_NPHIES].[dbo].DHSLab_Details ll
             inner join DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                on ll.proidclaim = ch.proidclaim
            inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where ch.CompanyCode = @InsuranceCode
              and CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.batchStartDate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt

        print 'lab Done'

------------------------------------------------------------------------------------------------------------------------------------------------------

       insert into [DHS-NPHIES].dbo.DHSRadiology_Details(ServiceCode,ServiceDescription,    
                                      ClinicalData,RadiologyResult,VisitDate,ResultDate,ProIdClaim)    
                              select ServiceCode,ServiceDescription,    
            rr.ClinicalData,left(RadiologyResult,1024),VisitDate,ResultDate,dch.ProIdClaim 

        from DHS.[DHS_NPHIES].[dbo].DHSRadiology_Details rr
             inner join  DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                on rr.proidclaim = ch.proidclaim
            inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where ch.CompanyCode = @InsuranceCode
              and CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.batchStartDate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt

        print 'radiology Done'

---------------------------------------------------------------------------------------------------------------------------------------------------------
 insert into [DHS-NPHIES].dbo.DHS_Attachment(Department,Location,AttachmentType,ContentType,Comments,ProIdClaim)    
            select Department,Location,AttachmentType,ContentType,Comments,dch.ProIdClaim   
                 from DHS.[DHS_NPHIES].[dbo].DHS_Attachment aa    
                   inner join DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                on aa.proidclaim = ch.proidclaim
            inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where ch.CompanyCode = @InsuranceCode
              and CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.batchStartDate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt 

	print 'Attachment Done'				    
  ------------------------------------------------------------------------------------------------------------------------------------------------
  INSERT INTO [DHS-NPHIES].dbo.DHS_Doctor
                      (
                       SCFHSNumber,
                       DoctorName,
                       BenType,
                       BenHead,
                       Specialty,
                       DoctorCode,
                       Doc_DOB,
                       DoctorType_Code,
                       Nationality_Code,
                       Religion_Code,
                       Doc_IDNo,
                       Doc_Certificate,
                       Doc_Phone,
                       PractitionerIdentifier,
                       Doc_HISRefID,
                       DoctorGender,
					   ProIdClaim)
          SELECT DISTINCT 
                       d.SCFHSNumber,
                       d.DoctorName,
                       d.BenType,
                       d.BenHead,
                       d.Specialty,
                       d.DoctorCode,
                       Doc_DOB,
                       DoctorType_Code,
                       Nationality_Code,
                       Religion_Code,
                       Doc_IDNo,
                       Doc_Certificate,
                       Doc_Phone,
                       PractitionerIdentifier,
                       Doc_HISRefID,
                       DoctorGender,
					   dch.[ProIdClaim]
           from DHS.[DHS_NPHIES].[dbo].DHS_Doctor d
						inner join DHS.[DHS_NPHIES].[dbo].DHSClaim_Header ch
                           on d.proidclaim = ch.proidclaim
                        inner join [DHS-NPHIES].dbo.DHSClaim_Header dch
                           on dch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS = ch.InvoiceNumber collate SQL_Latin1_General_CP1_CI_AS

        where ch.CompanyCode = @InsuranceCode
              and CONVERT(date, ch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, ch.invoicedate) <= @To_Date_fmt
              AND CONVERT(date, dch.batchStartDate) >= @From_Dt_fmt
              and CONVERT(date, dch.batchStartDate) <= @To_Date_fmt

  print 'Doctor Done'	

  -----------------------------------------------------------

  update  [DHS-NPHIES].[dbo].DHSClaim_Header
set Pulse=40
where Pulse<40


update  [DHS-NPHIES].[dbo].DHSClaim_Header
set Pulse=200
where Pulse>200


update  [DHS-NPHIES].[dbo].DHSClaim_Header
set Pulse=80
where Pulse is null

update  [DHS-NPHIES].[dbo].DHSClaim_Header
set DurationOFIllness=1
where DurationOFIllness<1


update  [DHS-NPHIES].[dbo].DHSClaim_Header
set DurationOFIllness=365
where DurationOFIllness>365


update  [DHS-NPHIES].[dbo].DHSClaim_Header
set OxygenSaturation = null
where OxygenSaturation = '0'

update  [DHS-NPHIES].[dbo].DHSService_Details
set PBMDuration='14'
where (PBMDuration = '0' or PBMDuration is null)

Print 'Vital signs update'
---------------------------------------------------------------

 delete [DHS-NPHIES].[dbo].DHSService_Details
         where ProIdClaim in (select ProIdClaim from [DHS-NPHIES].[dbo].DHSClaim_Header
         where CompanyCode = '611')
         and LineClaimedAmount = 0.00

         print 'Bupa 0 services delete'
  ----------------------------------------------------------------------------------------------------------------------
   If @@TRANCOUNT > 0

 commit Tran T1                                          
    
    
   End Try                                        
    
   begin catch                                        
    
  ROLLBACK TRAN T1                                        
    
        select ERROR_MESSAGE()                                         
    
        --EXEC Proc_inserterrordetails                                           
    
       'There is a problem in batch fetching please contact DHS Arabia (Contact No : 92 0000 958)'                                     
    
   end catch      
    
    
    
END 

